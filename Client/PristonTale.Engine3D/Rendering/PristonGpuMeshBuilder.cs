using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;

namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonGpuMeshBuilder
{
    private readonly GraphicsDevice _graphicsDevice;

    public PristonGpuMeshBuilder(GraphicsDevice graphicsDevice)
    {
        _graphicsDevice = graphicsDevice;
    }

    internal sealed record PreparedMesh(
        PristonStaticVertex[] Vertices,
        int[] Indices,
        PristonMeshDiagnostics Diagnostics,
        BoundingBox Bounds);

    public PristonGpuMesh CreateStaticMesh(PristonCornerVertex[] vertices)
    {
        return Upload(PrepareStaticMesh(vertices));
    }

    public PreparedMesh PrepareStaticMesh(PristonCornerVertex[] vertices)
    {
        var uniqueVertices = new List<PristonCornerVertex>(vertices.Length);
        var vertexLookup = new Dictionary<PristonCornerVertex, int>(vertices.Length);
        var indices = new int[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var vertex = vertices[i];
            if (!vertexLookup.TryGetValue(vertex, out var index))
            {
                index = uniqueVertices.Count;
                uniqueVertices.Add(vertex);
                vertexLookup.Add(vertex, index);
            }

            indices[i] = index;
        }

        return new PreparedMesh(
            BuildStaticVertices(uniqueVertices, indices),
            indices,
            CalculateDiagnostics(uniqueVertices, indices),
            PristonBinaryUtil.BoundsFrom(vertices.Select(static vertex => vertex.Position)));
    }

    internal static long EstimateBytes(PreparedMesh prepared)
    {
        if (prepared is null)
        {
            return 0;
        }

        var indexSize = prepared.Vertices.Length <= ushort.MaxValue ? sizeof(ushort) : sizeof(int);
        return (long)prepared.Vertices.Length * PristonStaticVertex.VertexDeclaration.VertexStride + (long)prepared.Indices.Length * indexSize;
    }

    public PristonGpuMesh Upload(PreparedMesh prepared)
    {
        var vertexBuffer = new VertexBuffer(_graphicsDevice, PristonStaticVertex.VertexDeclaration, prepared.Vertices.Length, BufferUsage.WriteOnly);
        try
        {
            vertexBuffer.SetData(prepared.Vertices);
            var indexBuffer = CreateIndexBuffer(prepared.Vertices.Length, prepared.Indices);
            return new PristonGpuMesh(
                vertexBuffer,
                indexBuffer,
                prepared.Indices.Length / 3,
                prepared.Vertices.Length,
                prepared.Indices.Length,
                hasNormals: true,
                prepared.Diagnostics,
                prepared.Bounds);
        }
        catch
        {
            vertexBuffer.Dispose();
            throw;
        }
    }

    private static PristonMeshDiagnostics CalculateDiagnostics(IReadOnlyList<PristonCornerVertex> vertices, int[] indices)
    {
        var degenerate = 0;
        var up = 0;
        var down = 0;
        var side = 0;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var a = vertices[indices[i]].Position;
            var b = vertices[indices[i + 1]].Position;
            var c = vertices[indices[i + 2]].Position;
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 0.000001f)
            {
                degenerate++;
                continue;
            }

            normal.Normalize();
            if (normal.Y > 0.35f)
                up++;
            else if (normal.Y < -0.35f)
                down++;
            else
                side++;
        }

        return new PristonMeshDiagnostics(degenerate, up, down, side);
    }

    private PristonStaticVertex[] BuildStaticVertices(IReadOnlyList<PristonCornerVertex> vertices, int[] indices)
    {
        var normals = new Vector3[vertices.Count];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var ia = indices[i];
            var ib = indices[i + 1];
            var ic = indices[i + 2];
            var a = vertices[ia].Position;
            var b = vertices[ib].Position;
            var c = vertices[ic].Position;
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 0.000001f)
                continue;

            normal.Normalize();
            normals[ia] += normal;
            normals[ib] += normal;
            normals[ic] += normal;
        }

        var staticVertices = new PristonStaticVertex[vertices.Count];
        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            var normal = vertex.Normal != Vector3.Zero ? vertex.Normal : normals[i];
            if (normal.LengthSquared() < 0.000001f)
                normal = Vector3.Up;
            else
                normal.Normalize();

            staticVertices[i] = new PristonStaticVertex(
                vertex.Position,
                normal,
                vertex.Color,
                vertex.Uv0,
                vertex.Uv1,
                vertex.Uv2);
        }

        return staticVertices;
    }

    private IndexBuffer CreateIndexBuffer(int vertexCount, int[] indices)
    {
        if (vertexCount <= ushort.MaxValue)
        {
            var shortIndices = new ushort[indices.Length];
            for (var i = 0; i < indices.Length; i++)
                shortIndices[i] = (ushort)indices[i];

            var indexBuffer = new IndexBuffer(
                _graphicsDevice,
                IndexElementSize.SixteenBits,
                shortIndices.Length,
                BufferUsage.WriteOnly);
            indexBuffer.SetData(shortIndices);
            return indexBuffer;
        }

        var wideIndexBuffer = new IndexBuffer(
            _graphicsDevice,
            IndexElementSize.ThirtyTwoBits,
            indices.Length,
            BufferUsage.WriteOnly);
        wideIndexBuffer.SetData(indices);
        return wideIndexBuffer;
    }
}
