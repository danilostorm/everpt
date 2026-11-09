using Microsoft.Xna.Framework;

namespace PristonTale.Engine3D.Physics;

public interface IPristonCollisionQuery
{
    bool TryGetFloorHeight(Vector3 position, float referenceHeight, float stepUp, out float height);
    bool TryTraceSegment(Vector3 start, Vector3 end, out Vector3 hitPoint, out Vector3 hitNormal);
}
