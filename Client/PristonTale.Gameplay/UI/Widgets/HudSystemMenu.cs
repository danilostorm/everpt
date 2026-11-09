using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

public enum HudSystemEntry
{
    Settings,
    Help,
    Exit
}

// The exit button's menu: the original's 128x64 ExitMenu.tga sheet (Help above Quit) at 698,504 with its hover plaques,
// and Etervale's settings plaque just above it.
public static class HudSystemMenu
{
    public const int X = 698;
    public const int Y = 504;
    private const int PlaqueWidth = 96;
    private const int PlaqueHeight = 19;
    private const int SettingsY = Y - PlaqueHeight - 2;
    // The rows of the sheet the original answers: Help on top, Quit at the bottom.
    private const int HelpY = Y;
    private const int ExitY = Y + 31;

    private static readonly string[] Hover = [null, HudArt.SystemHelp, HudArt.SystemExit];

    // The plaques; the sheet's lowest chain hangs over the menu buttons and takes no clicks.
    public static Rectangle Bounds => new(X, SettingsY, PlaqueWidth, ExitY + PlaqueHeight + 1 - SettingsY);

    public static Rectangle Entry(HudSystemEntry entry) => entry switch
    {
        HudSystemEntry.Settings => new Rectangle(X, SettingsY, PlaqueWidth, PlaqueHeight),
        HudSystemEntry.Help => new Rectangle(X, HelpY, PlaqueWidth, PlaqueHeight + 1),
        _ => new Rectangle(X, ExitY, PlaqueWidth, PlaqueHeight + 1)
    };

    public static HudSystemEntry? At(Point design)
    {
        for (var index = 0; index < Hover.Length; index++)
        {
            if (Entry((HudSystemEntry)index).Contains(design))
            {
                return (HudSystemEntry)index;
            }
        }

        return null;
    }

    public static void Draw(HudPainter painter, HudLayout layout, Point pointer)
    {
        var offset = layout.BottomOffset;
        painter.Sprite(HudArt.SystemMenu, X, Y + offset);
        painter.Sprite(HudArt.SystemSettings, X, SettingsY + offset);
        if (At(new Point(pointer.X, pointer.Y - offset)) is { } entry && Hover[(int)entry] is { } hover)
        {
            painter.Sprite(hover, X, Entry(entry).Y + offset);
        }
    }
}
