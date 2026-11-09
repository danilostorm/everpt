using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

// Where the windowed game window goes, in the desktop coordinates of MonoGame's Window.Position: the top-left of the
// client area, below the frame's title bar. The screen is the primary monitor, the only one MonoGame describes.
public static class WindowPlacement
{
    // The height kept free above the client area for the window frame's title bar, by which the window is dragged.
    public const int TitleBar = 32;

    // The position that centres the client on the screen; a client too tall for it keeps its title bar on screen.
    public static Point Centered(Point clientSize, Rectangle screen)
    {
        var x = screen.X + (screen.Width - clientSize.X) / 2;
        var y = screen.Y + Math.Max(TitleBar, (screen.Height - clientSize.Y) / 2);
        return new Point(x, y);
    }

    // Whether a client at this position shows its title bar on the screen, so the player can still drag it.
    public static bool TitleOnScreen(Point position, Point clientSize, Rectangle screen) =>
        position.Y - TitleBar >= screen.Top && position.Y <= screen.Bottom
        && position.X < screen.Right && position.X + clientSize.X > screen.Left;

    // Whether the whole client and its title bar are on the screen.
    public static bool OnScreen(Point position, Point clientSize, Rectangle screen) =>
        screen.Contains(new Rectangle(position.X, position.Y - TitleBar, clientSize.X, clientSize.Y + TitleBar));
}
