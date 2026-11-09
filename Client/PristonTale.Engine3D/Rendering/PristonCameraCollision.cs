using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Engine3D.Rendering;

public static class PristonCameraCollision
{
    public static Vector3 Resolve(Vector3 previous, Vector3 candidate, float focusHeight, PristonCameraSettings settings, IPristonCollisionQuery collision)
    {
        if (collision is null)
        {
            return candidate;
        }

        var position = previous;
        var remaining = candidate - previous;
        for (var iteration = 0; iteration < 3; iteration++)
        {
            var length = remaining.Length();
            if (length <= 0.0001f)
            {
                break;
            }

            var direction = remaining / length;
            var probeEnd = position + direction * (length + settings.CollisionWallPad);
            if (!collision.TryTraceSegment(position, probeEnd, out var hit, out var normal))
            {
                position += remaining;
                break;
            }

            var allowed = Math.Clamp(Vector3.Distance(position, hit) - settings.CollisionWallPad, 0f, length);
            position += direction * allowed;
            var leftover = remaining - direction * allowed;
            remaining = leftover - normal * Vector3.Dot(leftover, normal);
        }

        var floorReference = MathF.Max(focusHeight, MathF.Max(previous.Y, position.Y));
        if (collision.TryGetFloorHeight(position, floorReference, 0f, out var height))
        {
            position.Y = MathF.Max(position.Y, height + settings.CollisionFloorPad);
        }

        return position;
    }
}
