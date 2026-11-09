namespace PristonTale.Simulation.Items;

// The original's store grid: 9x9 cells of 22 pixels on the window the warehouse, the NPC shop and a personal shop share.
public static class StoreGrid
{
    public const int Columns = 9;
    public const int Rows = 9;

    public static Inventory Create() => new(Columns, Rows);
}
