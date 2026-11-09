using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class BatchUploader
{
    private readonly GraphicsDevice _graphicsDevice;

    public BatchUploader(GraphicsDevice graphicsDevice) => _graphicsDevice = graphicsDevice;

    public long UploadedBytes { get; private set; }

    public void BeginFrame()
    {
        UploadedBytes = 0;
    }

    public void Ensure(RenderBatch batch)
    {
        if (batch.Mesh != null)
        {
            return;
        }

        if (batch.DynamicBuffer == null)
        {
            batch.DynamicBuffer = new DynamicVertexBuffer(
                _graphicsDevice,
                PristonStaticVertex.VertexDeclaration,
                batch.Vertices.Length,
                BufferUsage.WriteOnly);
            batch.DynamicBufferDirty = true;
        }

        if (batch.DynamicBufferDirty)
        {
            batch.DynamicBuffer.SetData(batch.Vertices, 0, batch.Vertices.Length, SetDataOptions.Discard);
            UploadedBytes += (long)batch.Vertices.Length * PristonStaticVertex.VertexDeclaration.VertexStride;
            batch.DynamicBufferDirty = false;
        }
    }
}
