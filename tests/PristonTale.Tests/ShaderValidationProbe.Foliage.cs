using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

// GPU check of foliage coverage: a two-sided alpha-tested leaf material with a texel alpha of 0.5 (above the cut-off)
// is opaque, drawn over a red background with the material's own blend state: it covers the background exactly as an
// alpha of 1 does, in the lit pass and the pre-pass (which writes the depth the lit pass tests against).
internal sealed partial class ShaderValidationProbe
{
    private void VerifyFoliageCoverage()
    {
        using var half = Texture(new Color(90, 160, 60, 128));
        using var full = Texture(new Color(90, 160, 60, 255));
        var leafHalf = Leaf(half);
        var leafFull = Leaf(full);
        Assert.True(ReferenceEquals(leafHalf.BlendState, BlendState.Opaque) && leafHalf.Description.Translucency > 0f,
            "an alpha-tested leaf is not an opaque, translucent cut-out");
        _shaders.ApplyFrame(Frame(0.3f, 0.7f));
        var covered = DrawOver(leafHalf, half);
        var reference = DrawOver(leafFull, full);
        Assert.True(Vector3.Distance(covered, reference) < 0.004f && covered.X < 0.9f * ClearRed.X,
            $"a leaf texel above the cut-off is not opaque: {covered} against {reference}");
        _verified++;
    }

    private static readonly Vector3 ClearRed = new(1f, 0f, 0f);

    private Vector3 DrawOver(PristonMaterialRuntime material, Texture2D texture)
    {
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(255, 0, 0, 255));
        GraphicsDevice.BlendState = material.BlendState;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        var effect = _shaders.SelectStaticSurface(material, texture, Vector2.Zero, Vector4.Zero, Vector2.Zero);
        effect.CurrentTechnique.Passes[0].Apply();
        var vertices = Quad.Select(p => new PristonStaticVertex(p, Vector3.Up, Color.White, new Vector2(0.5f))).ToArray();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        GraphicsDevice.SetRenderTarget(null);
        _target.GetData(_pixels);
        var pixel = _pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        return new Vector3(pixel.X, pixel.Y, pixel.Z);
    }

    private static PristonMaterialRuntime Leaf(Texture2D texture)
    {
        var description = PristonMaterialAdapter.WithShape(MaterialAdapterTests.Describe(false, null), alphaTest: true, twoSided: true);
        return new PristonMaterialRuntime([texture], twoSided: true, transparent: false, alphaTest: true,
            blendType: PristonBlendType.Alpha, transparency: 0, textureFormState: 0, frameMask: 0, shiftFrameSpeed: 0,
            animationFrame: 0, selfIllum: false, invisible: false, water: false, blinking: false, windAmplitudeX: 0,
            windAmplitudeZ: 0, description: description);
    }
}
