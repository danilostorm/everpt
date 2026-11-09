using System.Text;
using PristonTale.Contracts.Social;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Simulation.PersonalShops;

// The original's personal shop: a character in a town lays up to 30 bag items on the store grid, each at a price per unit,
// and hangs a sign over its head. Only bag items it could drop sell, puzzle pieces never; its gold with the whole listing
// sold must stay within what it may carry. A sale moves the units and the gold at once, at the price the buyer was shown.
public static class PersonalShopRules
{
    public const long MaxPrice = 100_000_000;

    private static readonly string[] UnlistedFamilies = ["PZ1", "PZ2"];

    public static bool IsListable(PlayerCharacter character, ItemInstance item) =>
        ItemDropRules.CanDrop(character, item) && Listable(item.Definition.CatalogKey);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool IsListable(ItemKind kind, string key) => ItemDropRules.CanDrop(kind, key) && Listable(key);

    private static bool Listable(string key) => !ItemCodes.InFamily(key, UnlistedFamilies);

    // The gold a seller may still take in: what it may carry less what it holds and what its listing brings.
    public static long GoldRoom(int level, int tier, long money, long listingTotal) => MoneyLimits.CarryLimit(level, tier) - money - listingTotal;

    // What the listing brings when every unit sells, or -1 when a total passes MaxPrice.
    public static long Total(Inventory bag, IReadOnlyList<PersonalShopEntry> entries)
    {
        long total = 0;
        foreach (var entry in entries)
        {
            var anchor = bag.Find(entry.ItemId);
            var count = anchor >= 0 ? bag[anchor].Count : 0;
            var price = entry.UnitPrice * count;
            if (price > MaxPrice)
            {
                return -1;
            }

            total += price;
        }

        return total;
    }

    public static PersonalShopStatus TryOpen(PlayerCharacter seller, bool inTown, string sign, IReadOnlyList<PersonalShopEntry> entries, out PersonalShop shop)
    {
        shop = null;
        if (!inTown)
        {
            return PersonalShopStatus.NotInTown;
        }

        sign = sign?.Trim() ?? "";
        if (sign.Length == 0 || Encoding.UTF8.GetByteCount(sign) > PersonalShopLimits.MaxSignBytes
            || entries is null or { Count: 0 or > PersonalShopLimits.MaxEntries })
        {
            return PersonalShopStatus.Rejected;
        }

        var grid = StoreGrid.Create();
        foreach (var entry in entries)
        {
            var anchor = seller.Inventory.Find(entry.ItemId);
            if (anchor < 0 || entry.UnitPrice is < 1 or > MaxPrice || !grid.TryPlace(seller.Inventory[anchor], entry.Cell))
            {
                return PersonalShopStatus.Rejected;
            }

            if (!IsListable(seller, seller.Inventory[anchor]))
            {
                return PersonalShopStatus.NotListable;
            }
        }

        var total = Total(seller.Inventory, entries);
        if (total < 0)
        {
            return PersonalShopStatus.Rejected;
        }

        if (GoldRoom(seller.Level, seller.Tier, seller.Money, total) < 0)
        {
            return PersonalShopStatus.GoldLimit;
        }

        shop = new PersonalShop(sign, [.. entries]);
        return PersonalShopStatus.Ok;
    }

    // Moves Count units of the listed item from the seller's bag to the buyer's and its price the other way, on candidates.
    // The price and the units must be the ones the buyer was shown; otherwise the status names what changed.
    public static PersonalShopStatus TrySell(PersonalShop shop, PlayerCharacter seller, PlayerCharacter buyer, PersonalShopPurchase purchase, out long price)
    {
        price = 0;
        var anchor = seller.Inventory.Find(purchase.ItemId);
        if (shop.Find(purchase.ItemId) is not { } entry || anchor < 0)
        {
            return PersonalShopStatus.SoldOut;
        }

        var item = seller.Inventory[anchor];
        if (entry.UnitPrice != purchase.UnitPrice)
        {
            return PersonalShopStatus.PriceChanged;
        }

        if (!NpcShopRules.IsValidCount(item.Definition, purchase.Count))
        {
            return PersonalShopStatus.Rejected;
        }

        if (purchase.Count > item.Count)
        {
            return PersonalShopStatus.CountChanged;
        }

        price = entry.UnitPrice * purchase.Count;
        if (price > MaxPrice)
        {
            return PersonalShopStatus.Rejected;
        }

        if (buyer.Money < price)
        {
            return PersonalShopStatus.NotEnoughGold;
        }

        if (GoldRoom(seller.Level, seller.Tier, seller.Money, price) < 0)
        {
            return PersonalShopStatus.GoldLimit;
        }

        if (!CarriedWeight.CanTake(buyer, CarriedWeight.Of(item.Definition, purchase.Count)))
        {
            return PersonalShopStatus.OverWeight;
        }

        if (!buyer.Inventory.TryAdd(seller.Inventory.TakeFrom(anchor, purchase.Count)))
        {
            return PersonalShopStatus.NoSpace;
        }

        return seller.TryChangeMoney(price) && buyer.TryChangeMoney(-price) ? PersonalShopStatus.Ok : PersonalShopStatus.Rejected;
    }
}
