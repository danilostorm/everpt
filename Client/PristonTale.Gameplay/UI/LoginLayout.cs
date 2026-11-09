using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.UI;

public readonly record struct LoginLayout(float Scale, Rectangle Panel)
{
    public Rectangle Title => At(62, 267, 356, 40);
    public Rectangle Subtitle => At(62, 306, 356, 28);
    public Rectangle Account => At(78, 367, 324, 42);
    public Rectangle Password => At(78, 439, 324, 42);
    public Rectangle Login => At(78, 501, 324, 44);
    public Rectangle Status => At(78, 558, 324, 24);
    public Rectangle Hint => At(78, 586, 324, 18);

    public static LoginLayout Create(int width, int height)
    {
        var scale = Math.Max(0.1f, Math.Min(1, Math.Min((width - 24) / 480f, (height - 24) / 720f)));
        var x = (int)((width / scale - 480) / 2);
        var y = (int)((height / scale - 720) / 2);
        return new LoginLayout(scale, new Rectangle(x, y, 480, 720));
    }

    public Point Pointer(Point drawable) => new((int)(drawable.X / Scale), (int)(drawable.Y / Scale));

    private Rectangle At(int x, int y, int width, int height) => new(Panel.X + x, Panel.Y + y, width, height);
}
