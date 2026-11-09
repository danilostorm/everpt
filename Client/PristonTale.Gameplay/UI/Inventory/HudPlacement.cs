using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Inventory;

public enum HudPlacementKind
{
    // Over a window but nowhere the item can go: a click does nothing.
    None,
    // Above the inventory window: the item leaves the bag (a shop, the warehouse, the ground).
    Outside,
    Fits,
    // Over exactly one other item, which the carried one replaces and which is picked up in turn.
    Exchange,
    // Over a potion of the same kind, which the carried potions join.
    Merge,
    // Over two items, a slot of another kind, a slot whose requirements the owner does not meet, or an item the place
    // keeps out.
    Blocked
}

// Where a carried item would land on the bag, an equipment slot, a belt box or the warehouse. Highlight: the rectangle
// the original colours (the landing cells, slot or box, or the covered item). Covered: the item a click picks up in
// exchange, or joins. Belt: the potion belt box it lands in, or -1.
public readonly record struct HudPlacement(HudPlacementKind Kind, Rectangle Highlight, int Anchor = -1, EquipmentSlot? Slot = null,
    OwnedItemView Covered = null, int Belt = -1)
{
    // A click sets the item down here (on free cells or exchanged with the covered item).
    public bool SetsDown => Kind is HudPlacementKind.Fits or HudPlacementKind.Exchange;
}

// The original's item colours: light blue where the item lands, white on the item it would pick up in exchange (or join),
// red where it is refused or for an item the character cannot use; the item under the pointer, nothing carried, is
// yellow. Carried is the faint tint of an item another window holds: the warehouse's carried item, a shop's withheld one.
public static class HudPlacementColors
{
    public static readonly Color Fits = Color.FromNonPremultiplied(160, 235, 255, 128);
    public static readonly Color Exchange = Color.FromNonPremultiplied(255, 255, 255, 128);
    public static readonly Color Blocked = Color.FromNonPremultiplied(255, 0, 0, 64);
    public static readonly Color Selection = Color.FromNonPremultiplied(255, 200, 0, 128);
    public static readonly Color Carried = Color.White * 0.4f;

    public static Color Of(HudPlacementKind kind) => kind switch
    {
        HudPlacementKind.Fits => Fits,
        HudPlacementKind.Exchange or HudPlacementKind.Merge => Exchange,
        HudPlacementKind.Blocked => Blocked,
        _ => Color.Transparent
    };

    public static void Draw(HudPainter painter, HudPlacement placement)
    {
        var color = Of(placement.Kind);
        if (color != Color.Transparent)
        {
            painter.Fill(placement.Highlight, color);
        }
    }
}
