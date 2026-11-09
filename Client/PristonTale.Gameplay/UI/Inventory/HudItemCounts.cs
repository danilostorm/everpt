using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

// The stack counts drawn at an item's bottom-right corner (the bag, the belt, the warehouse) and the "Name (count)" its
// tooltip shows, built once per revision of the items they label so drawing allocates nothing. A single item shows no
// count.
public sealed class HudItemCounts
{
    private readonly Dictionary<Guid, string> _labels = new();
    private readonly Dictionary<Guid, OwnedItemView> _named = new();
    private long _revision = long.MinValue;

    public void Refresh(long revision, IReadOnlyList<OwnedItemView> items)
    {
        if (_revision == revision)
        {
            return;
        }

        _revision = revision;
        _labels.Clear();
        _named.Clear();
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            if (item.Count > 1)
            {
                _labels[item.InstanceId] = item.Count.ToString();
                _named[item.InstanceId] = item with { Name = $"{item.Name} ({item.Count})" };
            }
        }
    }

    public string Label(OwnedItemView item) => _labels.GetValueOrDefault(item.InstanceId, "");

    // The item as its tooltip names it: with its count when it is a stack.
    public OwnedItemView Named(OwnedItemView item) => _named.GetValueOrDefault(item.InstanceId, item);

    // The item's icon in its cells, its count over the corner.
    public void Draw(HudPainter painter, HudItemIcons icons, OwnedItemView item, Rectangle bounds, Color tint)
    {
        icons.Draw(painter, item.CatalogKey, bounds, tint);
        var label = Label(item);
        if (label.Length > 0)
        {
            painter.Text(label, bounds.Right - painter.Measure(label) - 1, bounds.Bottom - 13);
        }
    }
}
