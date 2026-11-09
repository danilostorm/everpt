using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Rendering;

internal readonly record struct PristonDirectionalLight(
    Vector3 Direction,
    Vector3 DiffuseColor);

public readonly record struct PristonPointLight(
    Vector3 Position,
    float Radius,
    Vector3 Color,
    float Intensity);

// The frame's light as the shaders take it: pre-exposed radiance of a white Lambertian surface (Lighting/PristonPhotometry).
// AmbientColor lights upward-facing surfaces (the sky), GroundAmbientColor downward-facing ones (the ground bounce).
internal readonly record struct PristonLighting(
    Vector3 AmbientColor,
    PristonDirectionalLight Sun,
    IReadOnlyList<PristonPointLight> PointLights,
    int TransientLightCount = 0,
    Vector3 GroundAmbientColor = default)
{
    public static readonly PristonLighting Default = new(
        new Vector3(0.16f, 0.18f, 0.24f),
        new PristonDirectionalLight(
            Vector3.Normalize(new Vector3(-0.35f, -0.85f, -0.45f)),
            new Vector3(0.34f, 0.38f, 0.55f)),
        [],
        0,
        new Vector3(0.09f, 0.1f, 0.13f));
}
