using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.Persistence.Sqlite;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class NpcPlacementMigrationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("baseline creates the complete NPC schema and preserves imported data on rerun", BaselineAsync);
    }

    private static async Task BaselineAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"priston-npc-baseline-{Guid.NewGuid():N}");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPristonSqlite(Path.Combine(directory, "world.db"));

        try
        {
            await using var provider = services.BuildServiceProvider();
            var migrator = provider.GetRequiredService<IDatabaseMigrator>();
            await migrator.MigrateAsync();

            var connections = provider.GetRequiredService<IDbConnectionFactory>();
            await using var connection = await connections.OpenAsync();
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"VersionInfo\" WHERE \"Version\" = 13") == 1,
                "the consolidated baseline version was not recorded");
            Assert.True(
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('npcs') WHERE name IN ('portrait_offset_y', 'portrait_offset_z')") == 2
                && await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('monsters') WHERE name IN ('portrait_offset_y', 'portrait_offset_z')") == 2,
                "the target-frame portrait columns are missing");
            Assert.True(
                await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name IN ('face_index', 'tier', 'hair_index', 'size_level')") == 4,
                "the baseline omitted character appearance columns");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name = 'derived_stats'") == 0
                && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_table_info('warehouses') WHERE name = 'buy_area_count'") == 0
                && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sqlite_master WHERE name = 'mailboxes'") == 0
                && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name = 'save_seq'") == 1
                && await connection.ExecuteScalarAsync<string>("SELECT dflt_value FROM pragma_table_info('characters') WHERE name = 'starter_gifts_received'") == "5"
                && await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_table_info('characters') WHERE name IN ('help_pet', 'help_pet_hidden')") == 2
                && await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('item_transfers', 'currency_sinks', 'server_events')") == 3,
                "a column or table nothing reads is still there, or the save sequence, the served starter gifts of a stored character or a game log is missing");
            Assert.True(await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM pragma_table_info('field_npcs') WHERE name IN ('quest_trigger', 'size_level')") == 0,
                "a placement still stores the class or size that belongs to its NPC");
            Assert.True(await connection.ExecuteScalarAsync<string>("SELECT name FROM languages WHERE code = 'en'") == "English",
                "English is not a catalog language");

            await connection.ExecuteAsync("INSERT INTO fields (stage, field_code) VALUES ('fixture', 3)");
            await connection.ExecuteAsync("INSERT INTO npcs (id, key, model) VALUES (124, 'Warehouse', 'fixture.inx')");
            await connection.ExecuteAsync(
                "INSERT INTO field_npcs (stage, record_index, npc_id, x, y, z, angle) VALUES ('fixture', 0, 124, 1, 2, 3, 4)");
            await connection.ExecuteAsync(
                "INSERT INTO motion_models (model_key, motion_table_key) VALUES ('char/npc/tn-010/tn-010.inx', 'custom/npc-motion.inx')");
            await connection.ExecuteAsync(
                """
                INSERT INTO motion_slots (
                    motion_table_key, slot, state, start_frame, end_frame,
                    event_frame0, event_frame1, event_frame2, event_frame3, repeat)
                VALUES ('custom/npc-motion.inx', 10, 80, 1, 9, 0, 0, 0, 0, 1)
                """);

            var versions = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"VersionInfo\"");
            await migrator.MigrateAsync();

            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"VersionInfo\"") == versions,
                "rerunning the migrations recorded another version");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT move_range FROM npcs WHERE key = 'Warehouse'") == 64,
                "the baseline omitted the NPC movement default");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT sound_code FROM npcs WHERE key = 'Warehouse'") == 0,
                "the baseline omitted the NPC sound-code column");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT y FROM field_npcs WHERE npc_id = 124") == 2,
                "rerunning the baseline changed an imported NPC position");
            Assert.True(
                await connection.ExecuteScalarAsync<string>(
                    "SELECT motion_table_key FROM motion_models WHERE model_key = 'char/npc/tn-010/tn-010.inx'") == "custom/npc-motion.inx",
                "rerunning the baseline replaced an imported motion mapping");

            var repository = provider.GetRequiredService<IWorldDataRepository>();
            var catalog = WorldDataFactory.ToMotionTimings(
                await repository.GetAllMotionModelsAsync(),
                await repository.GetAllMotionSlotsAsync());
            Assert.True(
                catalog.TryGet("char\\npc\\TN-010\\TN-010.ini", out var motions),
                "runtime motion catalog did not resolve the imported NPC model");
            Assert.True(motions.TryGetClip(MotionCode.Walk, out _), "runtime motion catalog omitted the Walk clip");
            Assert.True(
                await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM pragma_foreign_key_check") == 0,
                "the baseline broke foreign keys");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
