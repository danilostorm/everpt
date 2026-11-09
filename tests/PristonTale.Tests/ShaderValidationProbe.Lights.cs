using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;

namespace PristonTale.Tests;

// GPU check of the tiled light culling: a flat depth of 500 units, a light on the view axis at that depth (every tile
// it covers), one far behind the surfaces (culled by the depth range) and one beside the view (culled by the planes).
internal sealed partial class ShaderValidationProbe
{
    private void VerifyLightCulling()
    {
        const int width = 64;
        const int height = 64;
        using var depth = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Single, DepthFormat.None);
        GraphicsDevice.SetRenderTarget(depth);
        GraphicsDevice.Clear(ClearOptions.Target, new Vector4(500f), 1f, 0);
        GraphicsDevice.SetRenderTarget(null);
        using var effect = Content.Load<Effect>(PristonLightCulling.Asset).Clone();
        using var culling = new PristonLightCulling(GraphicsDevice, effect);
        var view = Matrix.CreateLookAt(Vector3.Zero, Vector3.Forward, Vector3.Up);
        var projection = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver2, 1f, 1f, 10000f);
        PristonPointLight[] lights =
        [
            new(new Vector3(0f, 0f, -500f), 100f, Vector3.One, 1f),
            new(new Vector3(0f, 0f, -2000f), 100f, Vector3.One, 1f),
            new(new Vector3(3000f, 0f, -500f), 100f, Vector3.One, 1f)
        ];
        var tiles = culling.Run(lights, view, projection, 1f, 10000f, width, height, depth, _white);
        var masks = new uint[tiles.TilesAcross * tiles.TilesAcross];
        tiles.Masks.GetData<uint>(masks.AsSpan());
        var centre = masks[(tiles.TilesAcross / 2) * tiles.TilesAcross + tiles.TilesAcross / 2];
        var corner = masks[0];
        Assert.True(centre == 1u && corner == 0u, $"light culling: centre tile mask {centre} (expected 1), corner {corner} (expected 0)");
        tiles = culling.Run(lights, view, projection, 1f, 10000f, width, height, null, _white);
        tiles.Masks.GetData<uint>(masks.AsSpan());
        centre = masks[(tiles.TilesAcross / 2) * tiles.TilesAcross + tiles.TilesAcross / 2];
        Assert.True(centre == 3u, $"light culling without depth: centre tile mask {centre} (expected 3)");
        _verified += 2;
    }
}
