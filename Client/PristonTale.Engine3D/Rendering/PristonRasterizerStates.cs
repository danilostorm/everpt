using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal static class PristonRasterizerStates
{
    // Stage and model winding requires CullClockwiseFace after the SMD and view-space conversions.
    public static readonly RasterizerState SingleSided = new()
    {
        CullMode = CullMode.CullClockwiseFace
    };

    public static readonly RasterizerState TwoSided = new()
    {
        CullMode = CullMode.None
    };
}

internal static class PristonSamplerStates
{
    public static readonly SamplerState AnisotropicWrap = new()
    {
        Filter = TextureFilter.Anisotropic,
        MaxAnisotropy = 16,
        AddressU = TextureAddressMode.Wrap,
        AddressV = TextureAddressMode.Wrap,
        AddressW = TextureAddressMode.Wrap
    };
}
