namespace PristonTale.Simulation.World.Loot;

public enum DropKind
{
    Nothing = 0,
    Gold = 1,
    Item = 2
}

public sealed record DropEntry(
    DropKind Kind,
    int Weight,
    uint ItemCode,
    int GoldMin,
    int GoldMax,
    bool IsBonus)
{
    public static DropEntry NothingSlot(int weight) =>
        new(DropKind.Nothing, weight, 0, 0, 0, IsBonus: false);

    public static DropEntry GoldSlot(int weight, int min, int max) =>
        new(DropKind.Gold, weight, 0, min, max, IsBonus: false);
}

public sealed record DropTable(
    IReadOnlyList<DropEntry> Entries,
    int MaxItemsPerKill,
    bool AllPlayersSeeDrops)
{
    public static readonly DropTable Empty = new([], 0, false);

    public int MainWeightTotal
    {
        get
        {
            var total = 0;
            foreach (var e in Entries)
                if (!e.IsBonus)
                    total += e.Weight;
            return total;
        }
    }

    public IEnumerable<DropEntry> MainEntries => Entries.Where(static e => !e.IsBonus);

    public IEnumerable<DropEntry> BonusEntries => Entries.Where(static e => e.IsBonus);
}
