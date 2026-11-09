using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

// The screen-space slide of a title billboard, in pixels of a 600-pixel-high view (y down), tick by tick at 70 Hz: every
// fourth tick it closes Step pixels towards its place (Step starts at 35 and loses 3 each time, down to 4); 45 ticks
// after it closed it rises 2 pixels a tick.
public static class VfxScreenSlide
{
    public const float ViewPixels = 600f;
    private const int MaxTicks = 2048;

    private static readonly Dictionary<float, Vector2[]> Paths = new();

    public static Vector2 At(float from, int ticks)
    {
        Vector2[] path;
        lock (Paths)
        {
            if (!Paths.TryGetValue(from, out path!))
            {
                path = Trace(from);
                Paths.Add(from, path);
            }
        }

        return path[Math.Clamp(ticks, 0, MaxTicks - 1)];
    }

    private static Vector2[] Trace(float from)
    {
        var path = new Vector2[MaxTicks];
        float x = from;
        float y = 0f;
        var step = 35f;
        var moves = 0;
        var stopped = 0;
        for (var tick = 0; tick < MaxTicks; tick++)
        {
            path[tick] = new Vector2(x, y);
            if (moves >= 4)
            {
                moves = 0;
                if (x < 0f)
                {
                    x = MathF.Min(0f, x + step);
                }
                else if (x > 0f)
                {
                    x = MathF.Max(0f, x - step);
                }

                step = step - 3f < 5f ? 4f : step - 3f;
            }

            if (x == 0f && stopped >= 45)
            {
                y -= 2f;
            }

            if (x == 0f)
            {
                stopped++;
            }

            moves++;
        }

        return path;
    }
}
