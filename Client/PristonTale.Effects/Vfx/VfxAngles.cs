using Microsoft.Xna.Framework;

namespace PristonTale.Effects.Vfx;

internal static class VfxAngles
{
    // RotationX(x) * RotationY(y) * RotationZ(z) written out (row vectors), without building and multiplying three matrices.
    public static Matrix Rotation(Vector3 degrees)
    {
        var (sx, cx) = MathF.SinCos(MathHelper.ToRadians(degrees.X));
        var (sy, cy) = MathF.SinCos(MathHelper.ToRadians(degrees.Y));
        var (sz, cz) = MathF.SinCos(MathHelper.ToRadians(degrees.Z));
        return new Matrix(
            cy * cz, cy * sz, -sy, 0f,
            sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy, 0f,
            cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy, 0f,
            0f, 0f, 0f, 1f);
    }
}
