using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace PristonTale.Engine3D.Rendering.Lighting;

// Look-development references beside the camera focus (ClientSettings.CalibrationSpheres): an 18 % grey diffuse sphere,
// whose sunlit side displays as mid grey under a correct exposure and light level, and a chrome sphere, which mirrors
// the image-based lighting and the sun's highlight. Both go through the ordinary lit pass.
internal sealed class PristonCalibrationSpheres
{
    public const float Radius = 22f;
    private const int Rings = 16;
    private const int Segments = 32;
    private static readonly Vector3 Lift = new(0f, 60f, 0f);
    private static readonly Vector3 Spacing = new(55f, 0f, 0f);

    private readonly PristonStaticVertex[] _unitSphere = BuildSphere();

    public PristonCalibrationSpheres(Texture2D white)
    {
        Grey = CreateBatch(white, new Vector4(0.18f, 0.18f, 0.18f, 1f), roughness: 0.6f, metallic: 0f);
        Chrome = CreateBatch(white, new Vector4(0.95f, 0.95f, 0.95f, 1f), roughness: 0.04f, metallic: 1f);
    }

    public RenderBatch Grey { get; }
    public RenderBatch Chrome { get; }

    // Moves both spheres beside the focus (vertices rewritten in world space for the dynamic batch path).
    public void Place(Vector3 focus)
    {
        Place(Grey, focus + Lift - Spacing);
        Place(Chrome, focus + Lift + Spacing);
    }

    private void Place(RenderBatch batch, Vector3 centre)
    {
        for (var i = 0; i < _unitSphere.Length; i++)
        {
            var unit = _unitSphere[i];
            batch.Vertices[i] = new PristonStaticVertex(centre + unit.Normal * Radius, unit.Normal, Color.White, Vector2.Zero);
        }

        batch.DynamicBufferDirty = true;
        batch.RefreshBounds();
    }

    private RenderBatch CreateBatch(Texture2D white, Vector4 baseColor, float roughness, float metallic)
    {
        var description = PristonMaterialDescription.Default with
        {
            BaseColorFactor = baseColor,
            Roughness = roughness,
            Metallic = metallic
        };
        var material = new PristonMaterialRuntime(
            [white],
            twoSided: true,
            transparent: false,
            alphaTest: false,
            blendType: 0,
            transparency: 0f,
            textureFormState: 0,
            frameMask: 0,
            shiftFrameSpeed: 0,
            animationFrame: 0,
            selfIllum: false,
            invisible: false,
            water: false,
            blinking: false,
            windAmplitudeX: 0f,
            windAmplitudeZ: 0f,
            description: description);
        var vertices = new PristonStaticVertex[_unitSphere.Length];
        Array.Copy(_unitSphere, vertices, vertices.Length);
        return new RenderBatch(vertices, [], null, material);
    }

    // A unit UV sphere as a triangle list (normal = position).
    private static PristonStaticVertex[] BuildSphere()
    {
        var vertices = new List<PristonStaticVertex>(Rings * Segments * 6);
        for (var ring = 0; ring < Rings; ring++)
        {
            for (var segment = 0; segment < Segments; segment++)
            {
                var a = Point(ring, segment);
                var b = Point(ring + 1, segment);
                var c = Point(ring + 1, segment + 1);
                var d = Point(ring, segment + 1);
                vertices.Add(Vertex(a));
                vertices.Add(Vertex(b));
                vertices.Add(Vertex(c));
                vertices.Add(Vertex(a));
                vertices.Add(Vertex(c));
                vertices.Add(Vertex(d));
            }
        }

        return vertices.ToArray();
    }

    private static Vector3 Point(int ring, int segment)
    {
        var theta = MathF.PI * ring / Rings;
        var phi = 2f * MathF.PI * segment / Segments;
        return new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
    }

    private static PristonStaticVertex Vertex(Vector3 unit) => new(unit, unit, Color.White, Vector2.Zero);
}
