using Microsoft.Xna.Framework;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.UI.Inventory;

// Where a carried item would go, as the original decides it: on the bag as HudGridPlacement says; an equipment slot or a
// potion belt box takes it when the item's frame, less half a cell on each side, overlaps it. A belt potion goes back to
// the bag only on free cells or onto its own kind.
public static class HudInventoryPlacement
{
    // The carried item leaves the bag when its frame ends above this line of the 800x600 design (the inventory window
    // and the bottom bar start below it).
    private const int OutsideLine = 420;

    public static HudPlacement Of(Point origin, IReadOnlyList<OwnedItemView> items, OwnedItemView carried, Point pointer, bool usable)
    {
        var frame = HudGridPlacement.Frame(carried, pointer);
        var half = HudGridPlacement.HalfCell;
        var inner = new Rectangle(frame.X + half, frame.Y + half, frame.Width - 2 * half, frame.Height - 2 * half);
        for (var slot = 0; slot < EquipmentSlots.Count; slot++)
        {
            var bounds = HudInventoryLayout.Equipment(origin, slot);
            if (inner.Intersects(bounds))
            {
                return Equipment(items, carried, (EquipmentSlot)slot, bounds, usable);
            }
        }

        for (var box = 0; box < PotionBelt.Boxes; box++)
        {
            var bounds = HudInventoryLayout.Belt(origin, box);
            if (inner.Intersects(bounds))
            {
                return Belt(items, carried, box, bounds);
            }
        }

        if (HudGridPlacement.Of(HudInventoryLayout.Bag(origin), HudInventoryLayout.Columns, HudInventoryLayout.Rows, items, HudInventoryLayout.InBag,
                carried, pointer) is { } placement)
        {
            return carried.BeltBox >= 0 && placement.Kind == HudPlacementKind.Exchange ? placement with { Kind = HudPlacementKind.Blocked } : placement;
        }

        return frame.Bottom < origin.Y + OutsideLine ? new HudPlacement(HudPlacementKind.Outside, Rectangle.Empty) : default;
    }

    // A warehouse item over the window: it goes only to the bag, on free cells or exchanged with one bag item the
    // warehouse takes in turn; a slot, a belt box, a stack of its kind or an item the warehouse keeps out refuses it.
    public static HudPlacement OfStored(Point origin, IReadOnlyList<OwnedItemView> items, OwnedItemView carried, Point pointer)
    {
        var placement = Of(origin, items, carried, pointer, true);
        return placement.Kind switch
        {
            HudPlacementKind.Outside => default,
            HudPlacementKind.Merge => placement with { Kind = HudPlacementKind.Blocked },
            _ when placement.Slot is not null || placement.Belt >= 0 => placement with { Kind = HudPlacementKind.Blocked },
            HudPlacementKind.Exchange when !WarehouseStorage.CanStore(placement.Covered.Kind, placement.Covered.CatalogKey) =>
                placement with { Kind = HudPlacementKind.Blocked },
            _ => placement
        };
    }

    private static HudPlacement Belt(IReadOnlyList<OwnedItemView> items, OwnedItemView carried, int box, Rectangle bounds)
    {
        if (carried.Kind != ItemKind.Potion)
        {
            return new HudPlacement(HudPlacementKind.Blocked, bounds, Belt: box);
        }

        var held = OwnedItemView.InBox(items, box);
        held = held == carried ? null : held;
        var kind = held is null ? HudPlacementKind.Fits : held.Joins(carried) ? HudPlacementKind.Merge : HudPlacementKind.Exchange;
        return new HudPlacement(kind, bounds, Covered: held, Belt: box);
    }

    private static HudPlacement Equipment(IReadOnlyList<OwnedItemView> items, OwnedItemView carried, EquipmentSlot slot, Rectangle bounds, bool usable)
    {
        if (!ItemEquipmentMetadata.Fits(carried.Kind, slot) || !usable)
        {
            return new HudPlacement(HudPlacementKind.Blocked, bounds, Slot: slot);
        }

        // A two-handed weapon shown in the off-hand slot is what an off-hand item picks up there, as the original exchanges
        // it.
        var covered = OwnedItemView.InSlot(items, (int)slot) ?? (slot == EquipmentSlot.OffHand ? HudInventoryLayout.TwoHandedShadow(items) : null);
        covered = covered == carried ? null : covered;

        return new HudPlacement(covered is null ? HudPlacementKind.Fits : HudPlacementKind.Exchange, bounds, Slot: slot, Covered: covered);
    }
}
