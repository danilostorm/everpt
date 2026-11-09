using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.PersonalShops;

// One item a seller offers: an item of its bag, the price of one unit (a stack sells by the unit) and the store grid cell
// the item shows at.
public readonly record struct PersonalShopEntry(Guid ItemId, long UnitPrice, int Cell);

// A seller's open shop: its sign and what it offers. The items stay in the seller's bag until sold; an entry whose item
// left the bag is dropped, and a shop with no entry left closes.
public sealed class PersonalShop
{
    private readonly List<PersonalShopEntry> _entries;

    internal PersonalShop(string sign, List<PersonalShopEntry> entries)
    {
        Sign = sign;
        _entries = entries;
    }

    public string Sign { get; }

    public IReadOnlyList<PersonalShopEntry> Entries => _entries;

    public bool IsEmpty => _entries.Count == 0;

    public PersonalShopEntry? Find(Guid itemId)
    {
        foreach (var entry in _entries)
        {
            if (entry.ItemId == itemId)
            {
                return entry;
            }
        }

        return null;
    }

    // Drops the entries whose item is no longer in the bag.
    public void Prune(Inventory bag) => _entries.RemoveAll(entry => bag.Find(entry.ItemId) < 0);
}
