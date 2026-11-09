using System.Data.Common;
using Dapper;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

public sealed partial class Importer
{
    private static async Task WriteNpcsAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedNpc> npcs,
        IReadOnlyDictionary<string, IdentityRegistry.Entry> npcIds, IReadOnlyDictionary<string, string> itemCodes, ImportReport report,
        CancellationToken ct)
    {
        foreach (var n in npcs)
        {
            var identity = npcIds[n.File];
            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO npcs (id, key, model, level, hostile, size_level, move_range, model_scale, sound_code, portrait_offset_y,
                    portrait_offset_z, appear_slot, appear_cycle)
                VALUES (@Id, @Key, @Model, @Level, @Hostile, @SizeLevel, @MoveRange, @ModelScale, @SoundCode, @PortraitOffsetY,
                    @PortraitOffsetZ, @AppearSlot, @AppearCycle)
                """,
                new
                {
                    identity.Id, identity.Key, n.Model, n.Level, n.Hostile, SizeLevel = n.SizeLevel ?? 0, n.MoveRange, n.ModelScale, n.SoundCode, n.PortraitOffsetY,
                    n.PortraitOffsetZ, n.AppearSlot, n.AppearCycle,
                }, tx, cancellationToken: ct));
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO npc_texts (npc_id, language, name) VALUES (@Id, @Language, @Name)",
                new { identity.Id, Language = CatalogLanguages.English, Name = n.DisplayName.Length > 0 ? n.DisplayName : identity.Key }, tx,
                cancellationToken: ct));
            report.NpcRows++;

            for (var ordinal = 0; ordinal < n.Dialog.Count; ordinal++)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_dialog_lines (npc_id, language, ordinal, text) VALUES (@Id, @Language, @Ordinal, @Text)",
                    new { identity.Id, Language = CatalogLanguages.English, Ordinal = ordinal, Text = n.Dialog[ordinal] }, tx, cancellationToken: ct));
                report.DialogLineRows++;
            }

            if (n.Dialog.Count > 0)
            {
                report.NpcDialogRows++;
            }

            for (var ordinal = 0; ordinal < n.Services.Count; ordinal++)
            {
                var service = n.Services[ordinal];
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_services (npc_id, ordinal, kind, value, value2, text) VALUES (@Id, @Ordinal, @Kind, @Value, @Value2, @Text)",
                    new { identity.Id, Ordinal = ordinal, Kind = (int)service.Kind, service.Value, service.Value2, service.Text }, tx, cancellationToken: ct));
                report.NpcServiceRows++;
            }

            foreach (var (tab, ordinal, code) in n.Shop)
            {
                if (!itemCodes.TryGetValue(code, out var canonical))
                {
                    // The empty-slot placeholder is recorded as "none"; the slot keeps its ordinal.
                    report.ShopItemCodesDangling++;
                    await RecordUnresolvedAsync(conn, tx, "npc_shop", identity.Key, code == KeyMaps.DropNone ? "none" : code,
                        new { Tab = tab, Ordinal = ordinal }, report, ct);
                    continue;
                }

                report.ShopItemCodesResolved++;
                await conn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO npc_shop_items (npc_id, tab, ordinal, item_code) VALUES (@Id, @Tab, @Ordinal, @Code)",
                    new { identity.Id, Tab = tab, Ordinal = ordinal, Code = canonical }, tx, cancellationToken: ct));
                report.NpcShopRows++;
            }
        }
    }
}
