using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

public enum PristonUiColorKey
{
    None,
    Black,
    Magenta
}

public static class PristonUiPixels
{
    public static void Prepare(DecodedPristonImage image, PristonUiColorKey key)
    {
        // Authored coverage owns the silhouette, opaque black details included.
        if (key == PristonUiColorKey.Black && image.HasSourceAlpha)
        {
            key = PristonUiColorKey.None;
        }

        foreach (ref var color in image.Pixels.AsSpan())
        {
            if (key == PristonUiColorKey.Black && color.R == 0 && color.G == 0 && color.B == 0)
            {
                color = Color.Transparent;
                continue;
            }

            if (key == PristonUiColorKey.Magenta)
            {
                // Generated matte may vary by a few RGB values. Neutral silver and crimson are not key colors.
                var spill = Math.Min(color.R, color.B) - color.G;
                if (spill > 60)
                {
                    var coverage = 255 - spill;
                    if (coverage < 16)
                    {
                        color = Color.Transparent;
                    }
                    else
                    {
                        color = new Color(Math.Max(0, color.R - spill), color.G, Math.Max(0, color.B - spill), coverage);
                    }

                    continue;
                }
            }

            color = Color.FromNonPremultiplied(color.R, color.G, color.B, color.A);
        }
    }
}
