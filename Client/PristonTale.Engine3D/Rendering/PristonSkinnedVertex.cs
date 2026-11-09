using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering;

// TEXCOORD1/2 carry the UVs of texture stages 1 and 2 from the face's texture link records.
internal struct PristonSkinnedVertex : IVertexType
{
    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Single, VertexElementUsage.BlendIndices, 0),
        new VertexElement(40, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1),
        new VertexElement(48, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 2));

    public PristonSkinnedVertex(Vector3 position, Vector3 normal, Color color, Vector2 texCoord, float boneIndex)
        : this(position, normal, color, texCoord, boneIndex, Vector2.Zero, Vector2.Zero)
    {
    }

    public PristonSkinnedVertex(
        Vector3 position,
        Vector3 normal,
        Color color,
        Vector2 texCoord,
        float boneIndex,
        Vector2 texCoord1,
        Vector2 texCoord2)
    {
        Position = position;
        Normal = normal;
        Color = color;
        TexCoord = texCoord;
        BoneIndex = boneIndex;
        TexCoord1 = texCoord1;
        TexCoord2 = texCoord2;
    }

    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
    public Vector2 TexCoord;
    public float BoneIndex;
    public Vector2 TexCoord1;
    public Vector2 TexCoord2;

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;
}
