using System.Data.Common;
using Dapper;
using PristonTale.Persistence.Abstractions.WorldData;
using PristonTale.WorldImport.Parsers;

namespace PristonTale.WorldImport;

public sealed partial class Importer
{
    private const string DefinitionInsert = """
        INSERT INTO item_definitions (
            item_code, variant, variant_ordinal, unique_item, spec_class, durability_min, durability_max, weight, price,
            damage_min_low, damage_min_high, damage_max_low, damage_max_high, attack_rating_min, attack_rating_max, critical,
            defence_min, defence_max, block_min, block_max, absorb_min, absorb_max, move_speed_min, move_speed_max,
            life_regen_min, life_regen_max, mana_regen_min, mana_regen_max, stamina_regen_min, stamina_regen_max,
            add_life_min, add_life_max, add_mana_min, add_mana_max, add_stamina_min, add_stamina_max,
            req_level, req_strength, req_spirit, req_talent, req_agility, req_health,
            restore_life_min, restore_life_max, restore_mana_min, restore_mana_max, restore_stamina_min, restore_stamina_max,
            shooting_range, attack_speed,
            res_bionic_min, res_bionic_max, res_earth_min, res_earth_max, res_fire_min, res_fire_max, res_ice_min, res_ice_max,
            res_lightning_min, res_lightning_max, res_poison_min, res_poison_max, res_water_min, res_water_max, res_wind_min, res_wind_max,
            max_stack, potion_space, magic_mastery_min, magic_mastery_max, glow_r, glow_g, glow_b, glow_a, glow_blink, glow_scale,
            display_effect, daily_drop_limit, set_code, set_pieces)
        VALUES (
            @Code, @Variant, @Ordinal, @Unique, @SpecClass, @DurabilityMin, @DurabilityMax, @Weight, @Price,
            @DamageMinLow, @DamageMinHigh, @DamageMaxLow, @DamageMaxHigh, @AttackRatingMin, @AttackRatingMax, @CriticalHit,
            @DefenceMin, @DefenceMax, @BlockMin, @BlockMax, @AbsorbMin, @AbsorbMax, @SpeedMin, @SpeedMax,
            @LifeRegenMin, @LifeRegenMax, @ManaRegenMin, @ManaRegenMax, @StaminaRegenMin, @StaminaRegenMax,
            @AddLifeMin, @AddLifeMax, @AddManaMin, @AddManaMax, @AddStaminaMin, @AddStaminaMax,
            @ReqLevel, @ReqStrength, @ReqSpirit, @ReqTalent, @ReqDexterity, @ReqHealth,
            @RestoreLifeMin, @RestoreLifeMax, @RestoreManaMin, @RestoreManaMax, @RestoreStaminaMin, @RestoreStaminaMax,
            @ShootingRange, @AttackSpeed,
            @R0Min, @R0Max, @R1Min, @R1Max, @R2Min, @R2Max, @R3Min, @R3Max,
            @R4Min, @R4Max, @R5Min, @R5Max, @R6Min, @R6Max, @R7Min, @R7Max,
            @MaxStack, @PotionSpace, @MagicMasteryMin, @MagicMasteryMax, @GlowR, @GlowG, @GlowB, @GlowA, @GlowBlink, @GlowScale,
            @DisplayEffect, @DailyDropLimit, @SetCode, @SetPieces)
        RETURNING id
        """;

    // Each item with its regular definition, then the quest versions and named uniques of items the catalog has.
    private async Task WriteItemsAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedItem> items,
        IReadOnlyDictionary<string, (int Width, int Height)> footprints, ImportReport report, CancellationToken ct)
    {
        var sets = await WriteItemSetsAsync(conn, tx, items, report, ct);
        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var footprint = footprints.TryGetValue(item.Code, out var size) ? size : (Width: 1, Height: 1);
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO items (code, kind, grid_width, grid_height) VALUES (@Code, @Kind, @Width, @Height)",
                new { item.Code, Kind = (int)item.Kind, footprint.Width, footprint.Height }, tx, cancellationToken: ct));
            codes.Add(item.Code);
            await WriteDefinitionAsync(conn, tx, item.Code, item, ItemVariant.Regular, 0, sets, report, ct);
        }

        foreach (var quest in _questVariants.Where(quest => codes.Contains(quest.Code)))
        {
            await WriteDefinitionAsync(conn, tx, Canonical(codes, quest.Code), quest, ItemVariant.Quest, 0, sets, report, ct);
        }

        foreach (var (ordinal, unique) in _namedUniques.Where(unique => codes.Contains(unique.Item.Code)))
        {
            await WriteDefinitionAsync(conn, tx, Canonical(codes, unique.Code), unique, ItemVariant.NamedUnique, ordinal, sets, report, ct);
        }
    }

    private static string Canonical(HashSet<string> codes, string code) => codes.TryGetValue(code, out var canonical) ? canonical : code;

    private static async Task WriteDefinitionAsync(DbConnection conn, DbTransaction tx, string code, ParsedItem it, ItemVariant variant, int ordinal,
        HashSet<int> sets, ImportReport report, CancellationToken ct)
    {
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition(DefinitionInsert, new
        {
            Code = code, Variant = (short)variant, Ordinal = ordinal, Unique = (int)it.UniqueItem, SpecClass = it.JobMask,
            it.DurabilityMin, it.DurabilityMax, it.Weight, it.Price,
            it.DamageMinLow, it.DamageMinHigh, it.DamageMaxLow, it.DamageMaxHigh, it.AttackRatingMin, it.AttackRatingMax, it.CriticalHit,
            it.DefenceMin, it.DefenceMax, it.BlockMin, it.BlockMax, it.AbsorbMin, it.AbsorbMax, it.SpeedMin, it.SpeedMax,
            it.LifeRegenMin, it.LifeRegenMax, it.ManaRegenMin, it.ManaRegenMax, it.StaminaRegenMin, it.StaminaRegenMax,
            it.AddLifeMin, it.AddLifeMax, it.AddManaMin, it.AddManaMax, it.AddStaminaMin, it.AddStaminaMax,
            it.ReqLevel, it.ReqStrength, it.ReqSpirit, it.ReqTalent, it.ReqDexterity, it.ReqHealth,
            it.RestoreLifeMin, it.RestoreLifeMax, it.RestoreManaMin, it.RestoreManaMax, it.RestoreStaminaMin, it.RestoreStaminaMax,
            it.ShootingRange, it.AttackSpeed,
            R0Min = it.ResMin[0], R0Max = it.ResMax[0], R1Min = it.ResMin[1], R1Max = it.ResMax[1],
            R2Min = it.ResMin[2], R2Max = it.ResMax[2], R3Min = it.ResMin[3], R3Max = it.ResMax[3],
            R4Min = it.ResMin[4], R4Max = it.ResMax[4], R5Min = it.ResMin[5], R5Max = it.ResMax[5],
            R6Min = it.ResMin[6], R6Max = it.ResMax[6], R7Min = it.ResMin[7], R7Max = it.ResMax[7],
            it.MaxStack, it.PotionSpace, it.MagicMasteryMin, it.MagicMasteryMax,
            GlowR = it.UniqueColor[0], GlowG = it.UniqueColor[1], GlowB = it.UniqueColor[2], GlowA = it.UniqueColor[3],
            GlowBlink = it.UniqueBlink, GlowScale = it.UniqueScale, it.DisplayEffect, it.DailyDropLimit,
            SetCode = sets.Contains(it.SetCode) ? it.SetCode : (int?)null, it.SetPieces,
        }, tx, cancellationToken: ct));
        report.ItemRows++;

        await conn.ExecuteAsync(new CommandDefinition(
            "INSERT INTO item_texts (definition_id, language, name) VALUES (@Id, @Language, @Name)",
            new { Id = id, Language = CatalogLanguages.English, it.Name }, tx, cancellationToken: ct));

        // A bonus written twice (two spellings of one key) keeps its last range.
        foreach (var (bonus, min, max) in it.Specials.GroupBy(static special => special.Bonus).Select(static group => group.Last()))
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO item_spec_bonuses (definition_id, bonus, min_value, max_value) VALUES (@Id, @Bonus, @Min, @Max)",
                new { Id = id, Bonus = (short)bonus, Min = min, Max = max }, tx, cancellationToken: ct));
            report.ItemSpecialRows++;
        }

        for (var index = 0; index < it.JobRandom.Count; index++)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO item_spec_random_classes (definition_id, ordinal, class) VALUES (@Id, @Ordinal, @Class)",
                new { Id = id, Ordinal = index, Class = it.JobRandom[index] }, tx, cancellationToken: ct));
            report.ItemJobRandomRows++;
        }
    }

    // One row per item set: the pieces worn for the bonus (the most any of its items asks) and the bonus its files state.
    private static async Task<HashSet<int>> WriteItemSetsAsync(DbConnection conn, DbTransaction tx, IReadOnlyList<ParsedItem> items, ImportReport report,
        CancellationToken ct)
    {
        var written = new HashSet<int>();
        foreach (var set in items.Where(static item => item.SetCode != 0).GroupBy(static item => item.SetCode).OrderBy(static set => set.Key))
        {
            var texts = set.Select(static item => item.SetBonus).Where(static text => text.Length > 0).Distinct(StringComparer.Ordinal).ToList();
            var text = texts.FirstOrDefault() ?? "";
            if (texts.Count > 1)
            {
                report.Warnings.Add($"item set {set.Key}: its items state different bonuses; the first is used");
            }

            if (!SetBonusParser.TryParse(text, out var bonus))
            {
                report.Warnings.Add($"item set {set.Key}: its bonus is not understood; the set gives nothing");
            }

            await conn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO item_sets (set_code, pieces, life, absorb, block, life_regen, mana_regen, stamina_regen)
                VALUES (@SetCode, @Pieces, @Life, @Absorb, @Block, @LifeRegen, @ManaRegen, @StaminaRegen)
                """,
                new
                {
                    SetCode = set.Key, Pieces = set.Max(static item => item.SetPieces), bonus.Life, bonus.Absorb, bonus.Block, bonus.LifeRegen,
                    bonus.ManaRegen, bonus.StaminaRegen,
                }, tx, cancellationToken: ct));
            written.Add(set.Key);
        }

        return written;
    }
}
