using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Post;

// One oversized triangle covering the viewport (Shaders/Core/Fullscreen.hlsli); uv (0, 0) is the top-left corner.
internal sealed class PristonFullscreenTriangle : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly VertexBuffer _triangle;

    public PristonFullscreenTriangle(GraphicsDevice device)
    {
        _device = device;
        _triangle = new VertexBuffer(device, VertexPositionTexture.VertexDeclaration, 3, BufferUsage.WriteOnly);
        _triangle.SetData(
        [
            new VertexPositionTexture(new Vector3(-1f, 1f, 0f), new Vector2(0f, 0f)),
            new VertexPositionTexture(new Vector3(3f, 1f, 0f), new Vector2(2f, 0f)),
            new VertexPositionTexture(new Vector3(-1f, -3f, 0f), new Vector2(0f, 2f))
        ]);
    }

    // Applies the effect's current technique and draws, with no blending unless the caller set it, no culling and the
    // given depth state (none by default).
    public void Draw(Effect effect, DepthStencilState depth = null)
    {
        _device.DepthStencilState = depth ?? DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        effect.CurrentTechnique.Passes[0].Apply();
        _device.SetVertexBuffer(_triangle);
        _device.DrawPrimitives(PrimitiveType.TriangleList, 0, 1);
    }

    public void Dispose() => _triangle.Dispose();
}
