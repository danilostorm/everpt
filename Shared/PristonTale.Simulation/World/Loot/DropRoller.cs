using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.World.Loot;

public readonly record struct RolledDrop(DropKind Kind, uint ItemCode, int Amount, bool ToInventory)
{
    // Rolled from the main weighted table (quest items from it reach only the lower class tiers).
    public bool FromMainTable { get; init; }

    // A puzzle piece that falls sealed as a lucky bag.
    public bool Sealed { get; init; }

    public static RolledDrop Gold(int amount) => new(DropKind.Gold, 0, amount, ToInventory: false);
    public static RolledDrop Item(uint code, bool toInventory = false) => new(DropKind.Item, code, 1, toInventory);
}

public static class DropRoller
{
    public static IReadOnlyList<RolledDrop> Roll(
        CombatRng rng,
        DropTable table,
        bool hasEventItem = false,
        uint eventItemCode = 0,
        int extraRollChance = 0,
        Func<RolledDrop?> eventRoll = null)
    {
        var drops = new List<RolledDrop>();

        if (hasEventItem && eventItemCode != 0)
            drops.Add(RolledDrop.Item(eventItemCode, toInventory: true));

        // Bonus Weight is a strict per-myriad chance and each entry rolls independently.
        foreach (var bonus in table.BonusEntries)
        {
            if (bonus.Kind == DropKind.Nothing)
                continue;
            if (rng.RollPerMyriad() < bonus.Weight)
                drops.Add(MaterializeEntry(rng, bonus, mainTable: false));
        }

        int mainTotal = table.MainWeightTotal;
        if (mainTotal > 0 && table.MaxItemsPerKill > 0)
        {
            // A drop-bonus item gives a monster that drops at all a percent chance of one more main roll.
            int rolls = table.MaxItemsPerKill;
            if (extraRollChance > 0 && rng.RollPercent() < extraRollChance)
            {
                rolls++;
            }

            for (int i = 0; i < rolls; i++)
            {
                // A running item event may replace the roll with its own item.
                if (eventRoll?.Invoke() is { } replaced)
                {
                    drops.Add(replaced);
                    continue;
                }

                var picked = PickWeightedMain(rng, table, mainTotal);
                if (picked is { Kind: not DropKind.Nothing })
                    drops.Add(MaterializeEntry(rng, picked, mainTable: true));
            }
        }

        return drops;
    }

    private static DropEntry PickWeightedMain(CombatRng rng, DropTable table, int mainTotal)
    {
        int roll = rng.Range(0, mainTotal - 1);
        int cursor = 0;
        foreach (var e in table.MainEntries)
        {
            cursor += e.Weight;
            if (roll < cursor)
                return e;
        }
        DropEntry last = null;
        foreach (var e in table.MainEntries)
            last = e;
        return last;
    }

    // Quest and ingredient items go to the credited player's pack instead of the ground.
    private static RolledDrop MaterializeEntry(CombatRng rng, DropEntry e, bool mainTable) => e.Kind switch
    {
        DropKind.Gold => RolledDrop.Gold(rng.Range(e.GoldMin, e.GoldMax)),
        DropKind.Item => RolledDrop.Item(e.ItemCode, Quests.QuestItems.IsQuestDrop(e.ItemCode)) with { FromMainTable = mainTable },
        _ => default
    };
}
