using Microsoft.Xna.Framework;
using NumVector3 = System.Numerics.Vector3;

namespace PristonTale.Engine3D;

// Authored data, the server and the wire use the game's (x, y, z); the client's render world mirrors z. The mirror is
// its own inverse, so the same call converts either way. A transport facing is the wire's heading of a forward; a
// render yaw is the CreateRotationY angle that turns a model's +z onto a render-space forward. The two relate by
// facing = pi - yaw.
public static class WorldSpace
{
    public static Vector3 Mirror(Vector3 point) => new(point.X, point.Y, -point.Z);

    public static NumVector3 Mirror(NumVector3 point) => new(point.X, point.Y, -point.Z);

    // The render plane (x, z) of a transport (x, z).
    public static Vector2 Planar(float x, float z) => new(x, -z);

    public static float MirrorFacing(float facing) => MathF.Atan2(MathF.Sin(facing), -MathF.Cos(facing));

    public static float TransportFacing(Vector3 forward) => MathF.Atan2(forward.X, -forward.Z);

    public static float RenderYaw(Vector3 forward) => MathF.Atan2(forward.X, forward.Z);
}
