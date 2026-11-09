using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Gameplay.World.Remote;

internal static class CursorPicking
{
    public static bool TryCreateRay(
        Viewport viewport,
        Matrix projection,
        Matrix view,
        Vector3 cameraPosition,
        Point cursor,
        out Ray ray)
    {
        ray = default;
        var far = viewport.Unproject(
            new Vector3(cursor.X, cursor.Y, PristonDepth.FarDepth),
            projection,
            view,
            Matrix.Identity);
        var direction = far - cameraPosition;
        var lengthSquared = direction.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        ray = new Ray(cameraPosition, direction);
        return true;
    }

    public static float? IntersectSphere(Ray ray, Vector3 center, float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
        {
            return null;
        }

        var sphere = new BoundingSphere(center, radius);
        if (sphere.Contains(ray.Position) != ContainmentType.Disjoint)
        {
            return null;
        }

        return ray.Intersects(sphere);
    }
}
