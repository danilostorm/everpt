using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Gameplay.World;

internal sealed class FieldCollisionQuery(IReadOnlyList<LoadedField> fields) : IPristonCollisionQuery
{
    public bool TryGetFloorHeight(Vector3 position, float referenceHeight, float stepUp, out float height)
    {
        height = 0;
        var found = false;
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].Collision.TryGetFloorHeight(position, referenceHeight, stepUp, out var candidate)
                && (!found || candidate > height))
            {
                height = candidate;
                found = true;
            }
        }

        return found;
    }

    public bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = default;
        hitNormal = default;
        var found = false;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < fields.Count; index++)
        {
            if (!fields[index].Collision.TryTraceSegment(start, end, out var candidate, out var normal))
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(start, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                hitPoint = candidate;
                hitNormal = normal;
                found = true;
            }
        }

        return found;
    }
}
