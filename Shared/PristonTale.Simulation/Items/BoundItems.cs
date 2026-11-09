using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Quests;

namespace PristonTale.Simulation.Items;

// The items the original never lets leave the character, neither on the ground nor in the warehouse: quest items, the
// weapon and ring a running rank-up holds, the quest ingredients (QuestItems.IsIngredient), the QW1 family and GF101.
public static class BoundItems
{
    private static readonly string[] Families = ["QW1"];

    // What the original keeps out of the warehouse and a trade besides the bound items: the event (SP1) and CH1 families.
    private static readonly string[] KeptFamilies = ["SP1", "CH1"];

    public static bool IsBound(PlayerCharacter character, ItemInstance item) =>
        IsBound(item.Definition.Kind, item.Definition.CatalogKey) || RankUpByQuestWeapon.IsBound(character, item);

    // The rule a view can check from the item's kind and key (the quest's own items are the server's to refuse).
    public static bool IsBound(ItemKind kind, string key) =>
        kind == ItemKind.Quest || key.Equals("GF101", StringComparison.OrdinalIgnoreCase) || ItemCodes.InFamily(key, Families)
        || ItemCodes.TryParse(key, out var code) && QuestItems.IsIngredient(code);

    // Whether the item may pass to the warehouse or to another player.
    public static bool CanHandOver(PlayerCharacter character, ItemInstance item) =>
        !IsBound(character, item) && !ItemCodes.InFamily(item.Definition.CatalogKey, KeptFamilies);

    public static bool CanHandOver(ItemKind kind, string key) => !IsBound(kind, key) && !ItemCodes.InFamily(key, KeptFamilies);
}
