using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

// GPU check of the lamps on actors: a skinned surface lit by one lamp only, its cube faces either empty or holding an
// occluder right in front of it. The occluded actor keeps the share of lamp light the soft shadow leaves (never black),
// and the light falls with the inverse square of the distance.
internal sealed partial class ShaderValidationProbe
{
    private void VerifyLampOnActor()
    {
        using var atlas = new RenderTarget2D(GraphicsDevice, 6 * 16, 16, false, SurfaceFormat.Single, DepthFormat.None);
        var lamp = new PristonPointLight(new Vector3(0f, 100f, 0.5f), 1000f, new Vector3(0.5f), 1f);
        var rows = new int[PristonShaderLibrary.MaxLocalLightCount];
        Array.Fill(rows, -1);
        rows[0] = 0;
        var lighting = new PristonLighting(Vector3.Zero, new PristonDirectionalLight(Vector3.Down, Vector3.Zero), [lamp]);
        var frame = Frame(0f, 0f) with
        {
            Lighting = lighting,
            LampShadows = new PristonLampShadowFrame(atlas, rows, 16, 1)
        };
        var material = Material(_grey, selfIllum: false);

        var open = LampLitValue(atlas, 1f, frame, material);
        var occluded = LampLitValue(atlas, 0f, frame, material);
        Assert.True(open > 0.05f, $"lamp-lit actor without occluder is dark: {open}");
        Assert.True(occluded > 0.1f * open && MathF.Abs(occluded / open - 0.2f) < 0.03f,
            $"lamp-lit actor behind an occluder: {occluded} against {open} (the soft shadow keeps a fifth)");

        // Twice as far, a quarter of the light (the window is still near 1 at a tenth of the radius).
        var far = frame with { Lighting = lighting with { PointLights = [lamp with { Position = new Vector3(0f, 200f, 0.5f) }] } };
        var halfway = LampLitValue(atlas, 1f, far, material);
        Assert.True(MathF.Abs(halfway / open - 0.25f) < 0.02f, $"lamp falloff at twice the distance: {halfway / open}");
        _verified += 3;
    }

    private float LampLitValue(RenderTarget2D atlas, float stored, PristonFrameContext frame, PristonMaterialRuntime material)
    {
        GraphicsDevice.SetRenderTarget(atlas);
        GraphicsDevice.Clear(ClearOptions.Target, new Vector4(stored), 1f, 0);
        GraphicsDevice.SetRenderTarget(null);
        _shaders.ApplyFrame(frame);
        var effect = _shaders.SelectSkinnedSurface(material, _grey, Vector2.Zero, _palette, Vector3.Zero, Vector2.Zero);
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(Color.Transparent);
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.DepthStencilState = DepthStencilState.None;
        GraphicsDevice.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        var vertices = Quad.Select(p => new PristonSkinnedVertex(p, Vector3.Up, Color.White, new Vector2(0.25f, 0.5f), 0)).ToArray();
        GraphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, 2);
        GraphicsDevice.SetRenderTarget(null);
        _target.GetData(_pixels);
        var pixel = _pixels[(TargetSize / 2) * TargetSize + TargetSize / 2];
        Assert.True(float.IsFinite(pixel.X + pixel.Y + pixel.Z), "lamp lighting produced a non-finite value");
        return pixel.Y;
    }
}
