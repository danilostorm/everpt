using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Post;

namespace PristonTale.Tests;

// GPU check of the screen-space reflections: a mirror-smooth floor seen from above reflects the sky, which no depth
// sample holds, so every ray misses and the scene keeps the image-based reflection the lit pass added (no change); a
// rough floor is skipped. A broken march or composition changes the image.
internal sealed partial class ShaderValidationProbe
{
    private void VerifyReflections()
    {
        const int size = 32;
        using var scene = new RenderTarget2D(GraphicsDevice, size, size, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        using var depth = new RenderTarget2D(GraphicsDevice, size, size, false, SurfaceFormat.Single, DepthFormat.None);
        using var normals = new RenderTarget2D(GraphicsDevice, size, size, false, SurfaceFormat.Color, DepthFormat.None);
        using var triangle = new PristonFullscreenTriangle(GraphicsDevice);
        using var effect = Content.Load<Effect>(PristonScreenReflections.Asset).Clone();
        using var reflections = new PristonScreenReflections(GraphicsDevice, triangle, effect);
        using var sh = new StructuredBuffer(GraphicsDevice, 16, 10, BufferShaderAccess.ReadWrite);
        var view = Matrix.CreateLookAt(new Vector3(0f, 500f, 0f), Vector3.Zero, Vector3.Forward);
        var projection = Matrix.CreatePerspectiveFieldOfView(1f, 1f, 1f, 10000f);
        foreach (var smoothness in new[] { 255, 0 })
        {
            GraphicsDevice.SetRenderTarget(depth);
            GraphicsDevice.Clear(ClearOptions.Target, new Vector4(500f), 1f, 0);
            GraphicsDevice.SetRenderTarget(normals);
            GraphicsDevice.Clear(new Color(128, 255, 128, smoothness));
            GraphicsDevice.SetRenderTarget(scene);
            GraphicsDevice.Clear(ClearOptions.Target, new Vector4(0.5f, 0.25f, 0.125f, 1f), 1f, 0);
            reflections.Run(scene, depth, normals, view, projection, default, _white, sh, 0f, () => GraphicsDevice.SetRenderTarget(scene));
            GraphicsDevice.SetRenderTarget(null);
            var pixels = new HalfVector4[size * size];
            scene.GetData(pixels);
            var centre = pixels[(size / 2) * size + size / 2].ToVector4();
            Assert.True(Vector3.Distance(new Vector3(centre.X, centre.Y, centre.Z), new Vector3(0.5f, 0.25f, 0.125f)) < 0.01f,
                $"reflections changed a scene whose rays all miss (smoothness {smoothness}): {centre}");
            _verified++;
        }
    }
}
