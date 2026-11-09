using Microsoft.Xna.Framework;
using PristonTale.Gameplay.UI.Rendering;

namespace PristonTale.Gameplay.UI.Widgets;

// Original notice: CutBox left/centre/right strip at y=5 with text at +43,+17, cleared after a few seconds.
public sealed class HudNotice
{
    public const double Seconds = 3;
    private string _text = "";
    private double _remaining;

    public string Text => _remaining > 0 ? _text : "";

    public void Show(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _text = text;
        _remaining = Seconds;
    }

    public void Update(double elapsedSeconds) => _remaining = Math.Max(0, _remaining - Math.Max(0, elapsedSeconds));

    public static Rectangle Bounds(HudLayout layout, int textWidth)
    {
        var centers = Math.Max(0, (textWidth + 22 + 15) / 16);
        var width = 64 + centers * 16;
        return new Rectangle((layout.Width - width) / 2, 5, width, 64);
    }

    public void Draw(HudPainter painter, HudLayout layout)
    {
        if (_remaining <= 0)
        {
            return;
        }

        var text = painter.Fit(_text, Math.Max(16, layout.Width - 120));
        var bounds = Bounds(layout, painter.Measure(text));
        painter.Sprite(HudArt.NoticeLeft, bounds.X, bounds.Y);
        var x = bounds.X + 32;
        for (; x < bounds.Right - 32; x += 16)
        {
            painter.Sprite(HudArt.NoticeCenter, x, bounds.Y);
        }

        painter.Sprite(HudArt.NoticeRight, x, bounds.Y);
        painter.Text(text, bounds.X + 43, bounds.Y + 17);
    }
}
