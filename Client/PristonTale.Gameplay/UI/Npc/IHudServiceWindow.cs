using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Inventory;
using PristonTale.Gameplay.UI.Rendering;
using PristonTale.Simulation.Characters;

namespace PristonTale.Gameplay.UI.Npc;

// What a service window draws with in one frame. Incoming is the item being dragged from the bag (null when none); a
// window takes input only while fully open and the settings panel is not over it. NowMs: the HUD clock, for windows that
// time something.
public readonly record struct HudServiceFrame(HudPainter Painter, HudLayout Layout, HudItemIcons Icons, OwnerState State, Point Pointer,
    OwnedItemView Incoming, bool SettingsOpen, double NowMs);

// A window an NPC service opens beside the bag (shop, warehouse, aging, mix). It slides in and out, takes the clicks over
// its area, draws its body with the panels and its overlay (tooltips, confirmations) above them. A work window keeps every
// click from the world while it is open; the shop keeps only its own area.
public interface IHudServiceWindow
{
    bool IsOpen { get; }
    bool Visible { get; }
    bool FullyOpen { get; }
    bool BlocksWorld { get; }

    void Advance(double seconds);

    bool Contains(Point pointer);

    void Hide();

    void Draw(in HudServiceFrame frame);

    void DrawOverlay(in HudServiceFrame frame);
}
