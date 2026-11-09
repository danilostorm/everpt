using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.Items;

// Carried weight: every owned item, equipped ones included. A potion weighs one per unit; other items weigh their
// authored weight per unit. Gold weighs nothing.
public static class CarriedWeight
{
    public static int Of(ItemInstance item) => Of(item.Definition, item.Count);

    public static int Of(ItemDefinition definition, int count) => Of(definition.Kind, definition.Weight, count);

    // The rule a view applies from an item's kind, authored weight and count.
    public static int Of(ItemKind kind, int weight, int count) => kind == ItemKind.Potion ? count : Math.Max(0, weight) * count;

    public static int Of(PlayerCharacter character)
    {
        var total = 0;
        foreach (var item in character.OwnedItems())
        {
            total += Of(item);
        }

        return total;
    }

    // An item may be taken while the carried weight plus its own stays within the limit.
    public static bool CanTake(PlayerCharacter character, int weight) => character.CarriedWeight + weight <= character.Derived.MaxWeight;

    public static bool IsOverweight(PlayerCharacter character) => character.CarriedWeight > character.Derived.MaxWeight;
}
