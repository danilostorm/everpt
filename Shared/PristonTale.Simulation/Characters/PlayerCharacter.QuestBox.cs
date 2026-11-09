using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The special item box (QuestItemBox) and how items reach the character: what it receives goes to the box when the box
// keeps it, as the original sets it, a potion to the belt, an item that can be worn on its empty slot, and to the bag
// otherwise: the page shown, else the other page.
public sealed partial class PlayerCharacter
{
    public QuestItemBox QuestBox { get; } = new();

    // An item the character receives (a pickup, a quest grant, a prize).
    public bool TryReceive(ItemInstance item)
    {
        if (QuestBox.TryAdd(item))
        {
            OnInventoryChanged();
            return true;
        }

        return TryReceiveOnBelt(item) || TryWearReceived(item) || Inventory.TryAdd(item) || TryAddToHiddenBag(item);
    }

    // The page not shown raises no change of its own (only the page shown is watched).
    private bool TryAddToHiddenBag(ItemInstance item)
    {
        if (!HiddenBag.TryAdd(item))
        {
            return false;
        }

        OnInventoryChanged();
        return true;
    }

    // A box item left in the bag (the box was full) into the first free box (InventoryAction.StoreQuestItem).
    public bool TryStoreQuestItem(Guid itemId)
    {
        var anchor = Inventory.Find(itemId);
        if (anchor < 0 || !QuestBox.TryAdd(Inventory[anchor]))
        {
            return false;
        }

        Inventory.TakeAt(anchor);
        return true;
    }

    public bool RestoreQuestBox(int box, ItemInstance item) => QuestBox.Restore(box, item);
}
