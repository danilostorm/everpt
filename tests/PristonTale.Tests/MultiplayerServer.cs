using System.Numerics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PristonTale.Application.World;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Server.Hosting;
using PristonTale.Server.Net;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// A whole game server in the test process: the real host composition on a disposable SQLite database, an ephemeral
// port, no game assets (fields run without terrain) and a synthetic world catalog swapped in before anyone joins.
internal sealed class MultiplayerServer : IAsyncDisposable
{
    public const string Password = "pw";
    // Forest 1: a hunting field next to Ricarten (village-2) through a gate at (2275, -14828).
    public const int HuntingField = 2;
    public const int Village = 3;
    public static readonly Vector3 Camp = new(2275f, 0f, -14600f);

    private static readonly Lazy<string> PasswordHash = new(() => PasswordHasher.Hash(Password));
    private static readonly CharacterClassRegistry Classes = new();
    private readonly IHost _host;
    private readonly string _directory;
    private int _accounts;

    private MultiplayerServer(IHost host, string directory, int port)
    {
        _host = host;
        _directory = directory;
        Address = $"tcp://127.0.0.1:{port}";
    }

    public string Address { get; }

    public WorldServer World => _host.Services.GetRequiredService<WorldServer>();

    // Runs a statement on the server's database, to put storage in the state a scenario needs.
    public async Task ExecuteAsync(string sql)
    {
        await using var connection = await _host.Services.GetRequiredService<IDbConnectionFactory>().OpenAsync();
        await Dapper.SqlMapper.ExecuteAsync(connection, sql);
    }

    public static async Task<MultiplayerServer> StartAsync(WorldData world, params (string Key, string Value)[] settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-multiplayer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var configuration = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:SqlitePath"] = Path.Combine(directory, "world.db"),
            ["Assets:Root"] = Path.Combine(directory, "no-assets"),
            ["Assets:AllowMissingTerrain"] = "true",
            ["Network:Port"] = "0",
            ["Network:MaxConnectionsPerIp"] = "0",
            ["World:ExperienceMultiplier"] = "1"
        };
        foreach (var (key, value) in settings)
        {
            configuration[key] = value;
        }

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.AddPristonServer();
        var host = builder.Build();
        try
        {
            await host.StartAsync();
            // The loader published the empty database's catalogs; fields read the holder when the first player arrives.
            host.Services.GetRequiredService<WorldDataHolder>().Current = world;
            var listener = host.Services.GetServices<IHostedService>().OfType<GameTcpServer>().Single();
            var port = await listener.BoundPort.WaitAsync(TimeSpan.FromSeconds(10));
            return new MultiplayerServer(host, directory, port);
        }
        catch
        {
            host.Dispose();
            DeleteDirectory(directory);
            throw;
        }
    }

    // One account with one character placed where the scenario needs it; returns the account name.
    public async Task<string> CreatePlayerAsync(PlayerSetup setup)
    {
        var accounts = _host.Services.GetRequiredService<IAccountRepository>();
        var characters = _host.Services.GetRequiredService<ICharacterRepository>();
        var userId = $"mp{Interlocked.Increment(ref _accounts):D3}{setup.Name}".ToLowerInvariant();
        var account = await accounts.CreateAsync(userId, PasswordHash.Value);
        var character = Classes.CreatePlayer(setup.Class, setup.Name, setup.Level);
        foreach (var (code, rank) in setup.Skills)
        {
            character.Skills.Restore(code, rank);
        }

        if (setup.Weapon is { } weapon)
        {
            character.Equip(EquipmentSlot.Weapon, new ItemInstance(weapon));
        }

        foreach (var item in setup.Items)
        {
            Assert.True(character.Inventory.TryAdd(new ItemInstance(item)), $"{item.CatalogKey} does not fit the inventory");
        }

        var starter = CharacterRecordFactory.NewStarter(Classes, account.Id, setup.Name, 0, setup.Class, setup.FieldId, level: setup.Level);
        await characters.CreateAsync(CharacterRecordFactory.FromLiveSession(starter, character, setup.FieldId, setup.Position));
        return userId;
    }

    // An account with no character, as a new player registers; returns the account name.
    public async Task<string> CreateAccountAsync(string name)
    {
        var userId = $"mp{Interlocked.Increment(ref _accounts):D3}{name}".ToLowerInvariant();
        await _host.Services.GetRequiredService<IAccountRepository>().CreateAsync(userId, PasswordHash.Value);
        return userId;
    }

    public async Task SetGameMasterAsync(string account, int level) =>
        Assert.True(await _host.Services.GetRequiredService<Persistence.Abstractions.GameMasters.IGameMasterRepository>().SetGmLevelAsync(account, level),
            $"no account {account}");

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            _host.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            DeleteDirectory(_directory);
        }
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static WorldData WorldWith(IReadOnlyDictionary<int, FieldSpawnData>? spawns = null, params MonsterDefinition[] monsters) =>
        new(new ItemCatalog([Staff, Firecracker, BigHead, Invulnerability, HopyCrystal]), new MonsterCatalog(monsters), new NpcCatalog([]), spawns ?? new Dictionary<int, FieldSpawnData>(),
            MotionTimingCatalog.Empty);

    // A spawn table that keeps one group of the given monster at a point beside the camp while players are near.
    public static Dictionary<int, FieldSpawnData> SpawnBeside(int monsterCode, Vector3 point, int count = 1) => new()
    {
        [HuntingField] = new FieldSpawnData([new SpawnPoint((int)point.X, (int)point.Z, true)], [new SpawnTableEntry(monsterCode, 1)], [], [])
        {
            MaxConcurrentMonsters = count,
            MaxPerSpawnPoint = count,
            SpawnPassIntervalMs = 50
        }
    };

    public static readonly ItemDefinition Firecracker = Usable("BI123");
    public static readonly ItemDefinition BigHead = Usable("BI109");
    public static readonly ItemDefinition Invulnerability = Usable("BI105");
    public static readonly ItemDefinition HopyCrystal = Usable("GP101");

    private static ItemDefinition Usable(string key) => new(ItemCodes.Parse(key), key, ItemKind.Misc, MaxStack: 1) { CatalogKey = key, Price = 100 };

    public static readonly ItemDefinition Staff = new(ItemCodes.Parse("WM101"), "Test staff", ItemKind.Staff, EquipmentSlot.Weapon)
    {
        CatalogKey = "WM101",
        BaseStats = new ItemStatBlock(4, 8, 0, 0, 0, 0, 0, 0, 0, 0)
    };
}

internal sealed record PlayerSetup(string Name, PlayerClass Class, int Level, int FieldId, Vector3 Position)
{
    public (uint Code, int Rank)[] Skills { get; init; } = [];
    public ItemDefinition? Weapon { get; init; }
    public ItemDefinition[] Items { get; init; } = [];
}
