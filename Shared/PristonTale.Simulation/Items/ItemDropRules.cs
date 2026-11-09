using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// The items a player cannot put on the ground, as the original refuses them: BoundItems, and the premium, event and
// time-limited items below (by catalog-key family, then by key).
public static class ItemDropRules
{
    private static readonly string[] Families = ["BI1", "BI2"];
    private static readonly HashSet<string> Keys = BuildKeys();

    public static bool CanDrop(PlayerCharacter character, ItemInstance item) =>
        !BoundItems.IsBound(character, item) && Droppable(item.Definition.CatalogKey);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool CanDrop(ItemKind kind, string key) => !BoundItems.IsBound(kind, key) && Droppable(key);

    private static bool Droppable(string key) => !Keys.Contains(key) && !ItemCodes.InFamily(key, Families);

    private static HashSet<string> BuildKeys()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "GF107", "GF108", "OR233", "OA136", "OA137" };
        foreach (var number in Enumerable.Range(31, 16).Concat([54, 55]))
        {
            keys.Add($"DA1{number}");
            keys.Add($"DA2{number}");
        }

        foreach (var number in Enumerable.Range(31, 4))
        {
            keys.Add($"OA2{number}");
            keys.Add($"DB1{number}");
        }

        return keys;
    }
}
