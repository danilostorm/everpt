namespace PristonTale.Simulation.Items;

// A row of boxes of one item each (the potion belt, the special item box), unlike the bag's grid of cells.
public class ItemSlots(int count)
{
    private readonly ItemInstance[] _items = new ItemInstance[count];

    public ItemInstance this[int slot] => _items[slot];

    public bool Contains(int slot) => slot >= 0 && slot < _items.Length;

    public int Find(Guid instanceId)
    {
        for (var slot = 0; slot < _items.Length; slot++)
        {
            if (_items[slot]?.InstanceId == instanceId)
            {
                return slot;
            }
        }

        return -1;
    }

    // The filled boxes with their items.
    public IEnumerable<(int Slot, ItemInstance Item)> Occupied()
    {
        for (var slot = 0; slot < _items.Length; slot++)
        {
            if (_items[slot] is { } item)
            {
                yield return (slot, item);
            }
        }
    }

    public ItemInstance TakeAt(int slot)
    {
        var item = Contains(slot) ? _items[slot] : null;
        if (item is not null)
        {
            _items[slot] = null;
        }

        return item;
    }

    internal void Set(int slot, ItemInstance item) => _items[slot] = item;

    internal void CopyFrom(ItemSlots other)
    {
        for (var slot = 0; slot < _items.Length; slot++)
        {
            _items[slot] = other._items[slot]?.Copy(other._items[slot].Count, preserveId: true);
        }
    }
}
