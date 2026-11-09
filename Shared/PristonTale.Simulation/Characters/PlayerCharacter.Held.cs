using PristonTale.Simulation.Items;

namespace PristonTale.Simulation.Characters;

// The item on the cursor that lies on neither bag page: a pickup with the bag open whose page shown has no room, as the
// original puts every such pickup on the mouse. It weighs, is saved, and stays held across a page switch; it leaves the
// hand set down on the page shown (onto free cells, a stack of its kind or over one item, which is held in its place) or
// thrown on the ground.
public sealed partial class PlayerCharacter
{
    public ItemInstance Held { get; private set; }

    // False while the hand holds an item already.
    public bool TryHold(ItemInstance item)
    {
        if (!RestoreHeld(item))
        {
            return false;
        }

        OnInventoryChanged();
        return true;
    }

    // As saved, without a change; false with a held item already.
    public bool RestoreHeld(ItemInstance item)
    {
        if (item is null || Held is not null)
        {
            return false;
        }

        Held = item;
        return true;
    }

    // The held item (by its id) set down with its top-left at anchor on the page shown: on free cells, all of it onto a
    // stack of its kind there, or over exactly one item, which is held instead.
    public bool TryPutDownHeld(Guid itemId, int anchor)
    {
        if (Held is not { } held || held.InstanceId != itemId)
        {
            return false;
        }

        if (Inventory.TryPlace(held, anchor))
        {
            Held = null;
            return true;
        }

        var covered = Inventory.Covered(anchor, held.Definition);
        if (covered < 0)
        {
            return false;
        }

        if (Inventory.TryJoin(covered, held))
        {
            Held = null;
            return true;
        }

        var displaced = Inventory[covered];
        if (!Inventory.TryExchange(covered, held, anchor))
        {
            return false;
        }

        Held = displaced;
        return true;
    }

    // Count of the held item out of the hand (to throw it): the item itself when that is all of it. Null when the id or
    // count does not match.
    public ItemInstance TakeHeld(Guid itemId, int count)
    {
        if (Held is not { } held || held.InstanceId != itemId || count < 1 || count > held.Count)
        {
            return null;
        }

        var whole = count == held.Count;
        var taken = whole ? held : held.Split(count);
        if (whole)
        {
            Held = null;
        }

        OnInventoryChanged();
        return taken;
    }
}
