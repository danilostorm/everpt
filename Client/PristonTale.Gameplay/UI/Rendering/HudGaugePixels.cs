using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.UI.Rendering;

public static class HudGaugePixels
{
    public static DecodedPristonImage Separate(DecodedPristonImage frame, Rectangle bounds)
    {
        if (!frame.IsValid || bounds.Width <= 0 || bounds.Height <= 0
            || !new Rectangle(0, 0, frame.Width, frame.Height).Contains(bounds))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        var pixels = new Color[bounds.Width * bounds.Height];
        var radiusX = bounds.Width * 0.5f;
        var radiusY = bounds.Height * 0.5f;
        for (var y = 0; y < bounds.Height; y++)
        {
            for (var x = 0; x < bounds.Width; x++)
            {
                var dx = (x + 0.5f - radiusX) / radiusX;
                var dy = (y + 0.5f - radiusY) / radiusY;
                var coverage = Math.Clamp((1f - MathF.Sqrt(dx * dx + dy * dy)) * Math.Min(radiusX, radiusY), 0, 1);
                var sourceIndex = (bounds.Y + y) * frame.Width + bounds.X + x;
                var original = frame.Pixels[sourceIndex];
                pixels[y * bounds.Width + x] = original * coverage;

                // Keep the glass highlights visible when empty, without a red ring outside the liquid.
                var luminance = original.R * 0.2126f + original.G * 0.7152f + original.B * 0.0722f;
                var empty = Color.FromNonPremultiplied((int)(24 + luminance * 0.18f), (int)(16 + luminance * 0.14f),
                    (int)(21 + luminance * 0.16f), original.A);
                frame.Pixels[sourceIndex] = Color.Lerp(original, empty, coverage);
            }
        }

        return new DecodedPristonImage(bounds.Width, bounds.Height, pixels, true);
    }

    public static DecodedPristonImage Crop(DecodedPristonImage image, Rectangle bounds)
    {
        if (!image.IsValid || bounds.Width <= 0 || bounds.Height <= 0
            || !new Rectangle(0, 0, image.Width, image.Height).Contains(bounds))
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }

        var pixels = new Color[bounds.Width * bounds.Height];
        for (var row = 0; row < bounds.Height; row++)
        {
            Array.Copy(image.Pixels, (bounds.Y + row) * image.Width + bounds.X, pixels, row * bounds.Width, bounds.Width);
        }

        return new DecodedPristonImage(bounds.Width, bounds.Height, pixels, image.HasSourceAlpha);
    }
}
