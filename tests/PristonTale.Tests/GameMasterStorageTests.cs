using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Accounts;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.GameMasters;
using PristonTale.Persistence.Abstractions.Security;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class GameMasterStorageTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("accounts store a game-master level that logins load, with an audit trail and character moves", StorageAsync);
    }

    private static async Task StorageAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-gm-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));
        try
        {
            await using var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<IDatabaseMigrator>().MigrateAsync();
            var accounts = provider.GetRequiredService<IAccountRepository>();
            var gameMasters = provider.GetRequiredService<IGameMasterRepository>();
            var created = await accounts.CreateAsync("keeper", PasswordHasher.Hash("secret"));
            Assert.True(created.GmLevel == 0, "a new account must be a player");
            Assert.True(await gameMasters.SetGmLevelAsync("keeper", 3) && !await gameMasters.SetGmLevelAsync("nobody", 2), "level writes by user id");
            Assert.True((await accounts.FindByUserIdAsync("keeper"))!.GmLevel == 3, "the account did not keep its level");
            var sessions = new SessionRegistry();
            var login = await new AuthAppService(sessions, accounts).LoginAsync("keeper", "secret");
            Assert.True(login.Ok && sessions.TryGet(login.SessionToken, out var session) && session.GmLevel == 3, "the login did not load the level");
            await gameMasters.AppendAuditAsync(new GmAuditEntry(created.Id, "keeper", "Keeper", 3, "/kick Ann", 2, true, "kicked"));
            await gameMasters.AppendAuditAsync(new GmAuditEntry(created.Id, "keeper", "Keeper", 3, "/@get WA101", 4, false, "denied: level too low"));
            var audit = await gameMasters.RecentAuditAsync(10);
            Assert.True(audit.Count == 2 && audit[0].Command == "/@get WA101" && !audit[0].Accepted && audit[0].RequiredLevel == 4
                && audit[1].Result == "kicked" && audit[1].CreatedAt.Year >= 2026, "the audit trail is not stored newest first");
            var classes = new CharacterClassRegistry();
            var characters = provider.GetRequiredService<ICharacterRepository>();
            var other = await accounts.CreateAsync("banker", PasswordHasher.Hash("x"));
            await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, created.Id, "Mover", 0, PlayerClass.Fighter, 3));
            await characters.CreateAsync(CharacterRecordFactory.NewStarter(classes, other.Id, "Stay", 0, PlayerClass.Fighter, 3));
            Assert.True(await gameMasters.MoveCharacterAsync("Mover", "banker", CharacterSlots.Maximum) == CharacterTransfer.Moved, "the move failed");
            var moved = await characters.ListByAccountAsync(other.Id);
            Assert.True(moved.Count == 2 && moved.Single(row => row.Name == "Mover").Slot == 1, "the character did not take the first free slot");
            Assert.True(await gameMasters.MoveCharacterAsync("Ghost", "banker", CharacterSlots.Maximum) == CharacterTransfer.NoCharacter
                && await gameMasters.MoveCharacterAsync("Mover", "nobody", CharacterSlots.Maximum) == CharacterTransfer.NoAccount
                && await gameMasters.MoveCharacterAsync("Mover", "keeper", 0) == CharacterTransfer.NoFreeSlot, "move refusals");
            await using var connection = await provider.GetRequiredService<IDbConnectionFactory>().OpenAsync();
            var version = await connection.ExecuteScalarAsync<long>("SELECT MAX(Version) FROM VersionInfo");
            Assert.True(version >= 19, $"migration 19 did not run (version {version})");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }
}
