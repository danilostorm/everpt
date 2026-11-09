using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

public static class PristonActorPalette
{
    public static void Fill(ReadOnlySpan<Matrix> skeleton, ReadOnlySpan<int> bones, Matrix world, bool attached, Span<Matrix> palette)
    {
        if (attached)
        {
            palette.Fill(world);
            return;
        }

        for (int index = 0; index < palette.Length; index++)
        {
            palette[index] = skeleton[bones[index]] * world;
        }
    }
}
