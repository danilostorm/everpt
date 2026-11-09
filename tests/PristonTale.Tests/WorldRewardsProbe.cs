using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.World;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Tests;

internal static class WorldRewardsProbe
{
    public static async Task RunAsync(string root, string disposableDatabase)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(disposableDatabase);
        await using var provider = services.BuildServiceProvider();
        var migrator = provider.GetRequiredService<IDatabaseMigrator>();
        await migrator.MigrateAsync();
        await migrator.MigrateAsync();
        var data = await new WorldDataAppService(provider.GetRequiredService<IWorldDataRepository>()).BuildAsync();
        var hopy = data.Monsters.All.Single(monster => monster.Key == "HOPY");
        Assert.True(hopy.ExpReward == 196, "real catalog XP mismatch");
        var hopyXp = KillReward.ExpForKill(hopy.ExpReward, hopy.Combat.Level, 1, 5);
        int drops = 0;
        foreach (var monster in data.Monsters.All)
        {
            foreach (var entry in monster.Drops.Entries)
            {
                if (entry.Kind == DropKind.Item)
                {
                    Assert.True(entry.ItemCode != 0 && data.Items.TryGet(entry.ItemCode, out _), "catalog drop code unresolved");
                    drops++;
                }
            }
        }

        var connections = provider.GetRequiredService<IDbConnectionFactory>();
        await using var connection = await connections.OpenAsync();
        var counts = (await connection.QueryAsync<FieldCount>(
            "SELECT f.field_code AS FieldCode, COUNT(*) AS Count FROM field_npcs n JOIN fields f ON f.stage = n.stage GROUP BY f.field_code ORDER BY f.field_code")).ToArray();
        Assert.True(counts.Single(field => field.FieldCode == 3).Count == 34, "Ricarten placements were not repaired");
        Assert.True(await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0, "migration broke FK integrity");
        var loader = new PristonActorLoader();
        foreach (var path in data.FieldSpawns.Values.SelectMany(field => field.FixedNpcs)
            .Select(placement => data.Npcs.Get(placement.NpcCode).ModelInxPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var actor = loader.Load(root, Path.Combine(root, path.Replace('\\', '/')));
            Assert.True(actor.Mesh.Objects.Count > 0, "NPC mesh empty");
        }

        foreach (var key in new[] { "GG101", "WA101", "PL101", "PS101" })
        {
            var actor = loader.LoadRigid(Path.Combine(root, EquipmentVisuals.MeshPath(key)));
            Console.WriteLine($"Ground {key}: bounds {actor.Mesh.Bounds.Min} .. {actor.Mesh.Bounds.Max}");
        }

        Console.WriteLine($"Verified {drops} item drop entries, {counts.Sum(field => field.Count)} NPC placements; Hopy = {hopyXp} XP.");
    }

    private sealed record FieldCount(int FieldCode, int Count);
}
