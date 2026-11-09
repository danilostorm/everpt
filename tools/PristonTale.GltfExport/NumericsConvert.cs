using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using NMatrix = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace PristonTale.GltfExport;

internal static class NumericsConvert
{
    public static NMatrix ToAffineNumerics(this XnaMatrix matrix)
    {
        return new NMatrix(
            matrix.M11,
            matrix.M12,
            matrix.M13,
            0f,
            matrix.M21,
            matrix.M22,
            matrix.M23,
            0f,
            matrix.M31,
            matrix.M32,
            matrix.M33,
            0f,
            matrix.M41,
            matrix.M42,
            matrix.M43,
            1f);
    }

    public static NVector2 ToNumerics(this XnaVector2 vector)
    {
        return new NVector2(vector.X, vector.Y);
    }

    public static NVector3 ToNumerics(this XnaVector3 vector)
    {
        return new NVector3(vector.X, vector.Y, vector.Z);
    }
}
