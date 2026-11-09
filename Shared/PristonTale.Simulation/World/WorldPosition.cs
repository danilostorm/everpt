using System.Numerics;

namespace PristonTale.Simulation.World;

public static class WorldPosition
{
    public const float CoordinateLimit = 1_000_000f;

    public static bool IsValid(Vector3 position)
    {
        return float.IsFinite(position.X)
            && float.IsFinite(position.Y)
            && float.IsFinite(position.Z)
            && MathF.Abs(position.X) <= CoordinateLimit
            && MathF.Abs(position.Y) <= CoordinateLimit
            && MathF.Abs(position.Z) <= CoordinateLimit;
    }
}
