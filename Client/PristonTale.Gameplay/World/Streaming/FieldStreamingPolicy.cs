using Microsoft.Xna.Framework;

namespace PristonTale.Gameplay.World.Streaming;

internal static class FieldStreamingPolicy
{
    public const int MaximumPendingFields = 2;
    public const int MaximumUploadSteps = 32;
    public const double UploadBudgetMilliseconds = 2d;
    public const int ScanIntervalFrames = 8;
    public const float IdlePrefetchDistance = 1024f;
    private const float MinimumApproachPrefetchDistance = 1400f;
    private const float MaximumApproachPrefetchDistance = 4500f;
    private const float LeadSeconds = 4f;
    private const float MinimumDirectionAlignment = 0.35f;
    private const float RetentionMargin = 1200f;

    public static float PrefetchDistance(float speed)
    {
        if (!float.IsFinite(speed) || speed < 0f)
        {
            return MinimumApproachPrefetchDistance;
        }

        return Math.Clamp(
            MinimumApproachPrefetchDistance + speed * LeadSeconds,
            MinimumApproachPrefetchDistance,
            MaximumApproachPrefetchDistance);
    }

    public static float RetentionDistance(float speed)
    {
        return PrefetchDistance(speed) + RetentionMargin;
    }

    public static bool ShouldPrefetch(
        Vector2 playerPosition,
        Vector2? movementTarget,
        Vector2 portalPosition,
        float speed)
    {
        if (!IsFinite(playerPosition) || !IsFinite(portalPosition))
        {
            return false;
        }

        var portalDelta = portalPosition - playerPosition;
        var portalDistanceSquared = portalDelta.LengthSquared();
        if (portalDistanceSquared <= IdlePrefetchDistance * IdlePrefetchDistance)
        {
            return true;
        }

        if (movementTarget is not { } target || !IsFinite(target))
        {
            return false;
        }

        var movement = target - playerPosition;
        var movementLengthSquared = movement.LengthSquared();
        if (movementLengthSquared < 0.0001f || portalDistanceSquared < 0.0001f)
        {
            return false;
        }

        var alignment = Vector2.Dot(movement, portalDelta) / MathF.Sqrt(movementLengthSquared * portalDistanceSquared);
        if (alignment < MinimumDirectionAlignment)
        {
            return false;
        }

        if (Vector2.DistanceSquared(target, portalPosition) >= portalDistanceSquared)
        {
            return false;
        }

        var distance = PrefetchDistance(speed);
        return portalDistanceSquared <= distance * distance;
    }

    private static bool IsFinite(Vector2 value)
    {
        return float.IsFinite(value.X) && float.IsFinite(value.Y);
    }
}
