using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonGpuMesh : IDisposable
{
    public PristonGpuMesh(
        VertexBuffer vertexBuffer,
        IndexBuffer indexBuffer,
        int primitiveCount,
        int vertexCount,
        int indexCount,
        bool hasNormals,
        PristonMeshDiagnostics diagnostics,
        BoundingBox bounds)
    {
        VertexBuffer = vertexBuffer;
        IndexBuffer = indexBuffer;
        PrimitiveCount = primitiveCount;
        VertexCount = vertexCount;
        IndexCount = indexCount;
        HasNormals = hasNormals;
        Diagnostics = diagnostics;
        Bounds = bounds;
    }

    public VertexBuffer VertexBuffer { get; }
    public IndexBuffer IndexBuffer { get; }
    public int PrimitiveCount { get; }
    public int VertexCount { get; }
    public int IndexCount { get; }
    public bool HasNormals { get; }
    public PristonMeshDiagnostics Diagnostics { get; }
    public BoundingBox Bounds { get; }

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
