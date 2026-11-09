using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// Stage and scenery vertex: COLOR0 is the authored stage colour and opacity, TEXCOORD1/2 the UVs of the material's
// texture layers 1 and 2 (each layer's own texture link, not the base layer's).
internal struct PristonStaticVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(44, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 2));

    public PristonStaticVertex(Vector3 position, Vector3 normal, Color color, Vector2 textureCoordinate)
        : this(position, normal, color, textureCoordinate, Vector2.Zero, Vector2.Zero)
    {
    }

    public PristonStaticVertex(
        Vector3 position,
        Vector3 normal,
        Color color,
        Vector2 textureCoordinate,
        Vector2 textureCoordinate1,
        Vector2 textureCoordinate2)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TextureCoordinate = textureCoordinate;
        TextureCoordinate1 = textureCoordinate1;
        TextureCoordinate2 = textureCoordinate2;
    }

    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
    public Vector2 TextureCoordinate;
    public Vector2 TextureCoordinate1;
    public Vector2 TextureCoordinate2;

    readonly VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}

internal static class PristonVertexNormals
{
    public static void RecalculateTriangleNormals(PristonStaticVertex[] vertices)
    {
        for (var i = 0; i + 2 < vertices.Length; i += 3)
        {
            var normal = Vector3.Cross(
                vertices[i + 1].Position - vertices[i].Position,
                vertices[i + 2].Position - vertices[i].Position);
            if (normal.LengthSquared() < 0.000001f)
            {
                normal = Vector3.Up;
            }
            else
            {
                normal.Normalize();
            }

            vertices[i].Normal = normal;
            vertices[i + 1].Normal = normal;
            vertices[i + 2].Normal = normal;
        }
    }
}
