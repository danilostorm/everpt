using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering.Post;

// CPU reference of the display transforms of Shaders/Passes/PostFinal.fx (exposed scene colour to linear display
// colour), for the shader checks and tools.
internal static class PristonDisplayTransform
{
    public static Vector3 Apply(TonemapperMode mode, Vector3 colour) => mode switch
    {
        TonemapperMode.AgX => Agx(colour),
        TonemapperMode.Aces => Aces(colour),
        _ => Vector3.Clamp(PbrNeutral(Vector3.Max(colour, Vector3.Zero)), Vector3.Zero, Vector3.One)
    };

    // Mesopic vision of the final pass, before the exposure: pre-exposed radiance blended toward its scotopic luminance in
    // the night's hue (unit luminance) by 0.04 / (0.04 + Y) of its absolute luminance Y (cd/m^2).
    public static Vector3 Mesopic(Vector3 c, float inversePreExposure, Vector3 nightHue)
    {
        var x = 0.4124f * c.X + 0.3576f * c.Y + 0.1805f * c.Z;
        var y = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        var z = 0.0193f * c.X + 0.1192f * c.Y + 0.9505f * c.Z;
        var scotopic = MathF.Max(y * (1.33f * (1f + (y + z) / MathF.Max(x, 1e-6f)) - 1.68f), 0f) / 2.5731f;
        var rods = 0.04f / (0.04f + y * inversePreExposure);
        return Vector3.Lerp(c, nightHue * scotopic, rods);
    }

    // Khronos PBR Neutral (2024).
    public static Vector3 PbrNeutral(Vector3 c)
    {
        const float startCompression = 0.8f - 0.04f;
        const float desaturation = 0.15f;
        var x = MathF.Min(c.X, MathF.Min(c.Y, c.Z));
        var offset = x < 0.08f ? x - 6.25f * x * x : 0.04f;
        c -= new Vector3(offset);
        var peak = MathF.Max(c.X, MathF.Max(c.Y, c.Z));
        if (peak < startCompression)
        {
            return c;
        }

        const float d = 1f - startCompression;
        var newPeak = 1f - d * d / (peak + d - startCompression);
        c *= newPeak / peak;
        var g = 1f - 1f / (desaturation * (peak - newPeak) + 1f);
        return Vector3.Lerp(c, new Vector3(newPeak), g);
    }

    public static Vector3 Agx(Vector3 c)
    {
        var v = Vector3.Max(c, new Vector3(1e-6f));
        v = new Vector3(
            0.842479062253094f * v.X + 0.0784335999999992f * v.Y + 0.0792237451477643f * v.Z,
            0.0423282422610123f * v.X + 0.878468636469772f * v.Y + 0.0791661274605434f * v.Z,
            0.0423756549057051f * v.X + 0.0784336f * v.Y + 0.879142973793104f * v.Z);
        v = new Vector3(AgxEncode(v.X), AgxEncode(v.Y), AgxEncode(v.Z));
        v = new Vector3(AgxContrast(v.X), AgxContrast(v.Y), AgxContrast(v.Z));
        v = new Vector3(
            1.19687900512017f * v.X - 0.0980208811401368f * v.Y - 0.0990297440797205f * v.Z,
            -0.0528968517574562f * v.X + 1.15190312990417f * v.Y - 0.0989611768448433f * v.Z,
            -0.0529716355144438f * v.X - 0.0980434501171241f * v.Y + 1.15107367264116f * v.Z);
        v = Vector3.Clamp(v, Vector3.Zero, Vector3.One);
        return new Vector3(MathF.Pow(v.X, 2.2f), MathF.Pow(v.Y, 2.2f), MathF.Pow(v.Z, 2.2f));
    }

    public static Vector3 Aces(Vector3 c)
    {
        const float exposure = 1.6f;
        var v = Vector3.Max(c, Vector3.Zero) * exposure;
        v = new Vector3(
            0.59719f * v.X + 0.35458f * v.Y + 0.04823f * v.Z,
            0.07600f * v.X + 0.90834f * v.Y + 0.01566f * v.Z,
            0.02840f * v.X + 0.13383f * v.Y + 0.83777f * v.Z);
        var a = v * (v + new Vector3(0.0245786f)) - new Vector3(0.000090537f);
        var b = v * (0.983729f * v + new Vector3(0.4329510f)) + new Vector3(0.238081f);
        v = a / b;
        v = new Vector3(
            1.60475f * v.X - 0.53108f * v.Y - 0.07367f * v.Z,
            -0.10208f * v.X + 1.10813f * v.Y - 0.00605f * v.Z,
            -0.00327f * v.X - 0.07276f * v.Y + 1.07602f * v.Z);
        return Vector3.Clamp(v, Vector3.Zero, Vector3.One);
    }

    private static float AgxEncode(float value) =>
        (MathHelper.Clamp(MathF.Log2(value), -12.47393f, 4.026069f) + 12.47393f) / 16.499999f;

    private static float AgxContrast(float x)
    {
        var x2 = x * x;
        var x4 = x2 * x2;
        return 15.5f * x4 * x2 - 40.14f * x4 * x + 31.96f * x4 - 6.868f * x2 * x + 0.4298f * x2 + 0.1191f * x - 0.00232f;
    }
}
