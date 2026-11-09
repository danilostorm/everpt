namespace PristonTale.Simulation.Items;

// The original's special item box, opened by the inventory button left of the shop button: the quest items, their
// ingredients and the QW1 items kept apart from the bag in 18 boxes of one item each, 6 columns by 3 rows over the bag's
// place. An item goes into the first free box down each column, then the next column; a full box leaves it to the bag.
// Owner views give a box item the slot FirstOwnerSlot + its box (row * Columns + column).
public sealed class QuestItemBox() : ItemSlots(Boxes)
{
    public const int Columns = 6;
    public const int Rows = 3;
    public const int Boxes = Columns * Rows;
    public const int FirstOwnerSlot = PotionBelt.FirstOwnerSlot + PotionBelt.Boxes;

    public static bool Keeps(uint code) => Quests.QuestItems.Boxed(code) || World.WingGates.IsWing(code);

    public static bool Keeps(string key) => ItemCodes.TryParse(key, out var code) && Keeps(code);

    // One unit per box, kept as the bag keeps an item (a copy with its id).
    public bool TryAdd(ItemInstance item)
    {
        if (item is not { Count: 1 } || !Keeps(item.Definition.Code) || Find(item.InstanceId) >= 0)
        {
            return false;
        }

        for (var column = 0; column < Columns; column++)
        {
            for (var row = 0; row < Rows; row++)
            {
                var box = row * Columns + column;
                if (this[box] is null)
                {
                    Set(box, item.Copy(1, preserveId: true));
                    return true;
                }
            }
        }

        return false;
    }

    // A saved box's item, as loaded: one unit the box keeps, in an empty box.
    public bool Restore(int box, ItemInstance item)
    {
        if (!Contains(box) || this[box] is not null || item is not { Count: 1 } || !Keeps(item.Definition.Code))
        {
            return false;
        }

        Set(box, item);
        return true;
    }
}
