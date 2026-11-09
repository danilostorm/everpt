using Microsoft.Xna.Framework;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Gameplay.World;

internal readonly record struct NpcApproachDecision(
    bool BeginApproach,
    bool StopLocally,
    bool SendStop,
    bool OpenDialog,
    Vector2 Destination);

internal static class NpcApproach
{
    private const float StopDistance = NpcInteraction.Reach * 0.65f;
    private const float ConfirmedReach = NpcInteraction.Reach * 0.9f;
    private const float DestinationMargin = 4f;

    public static NpcApproachDecision Evaluate(
        Vector2 localPosition,
        Vector2 serverPosition,
        Vector2 targetPosition,
        bool movementIntentSent)
    {
        var targetDelta = targetPosition - localPosition;
        if (targetDelta.LengthSquared() > StopDistance * StopDistance)
        {
            if (movementIntentSent)
            {
                return default;
            }

            var destination = targetPosition - Vector2.Normalize(targetDelta) * (StopDistance - DestinationMargin);
            return new NpcApproachDecision(true, false, false, false, destination);
        }

        var serverDistanceSquared = Vector2.DistanceSquared(targetPosition, serverPosition);
        var authorityIsInReach = serverDistanceSquared <= ConfirmedReach * ConfirmedReach;
        return new NpcApproachDecision(
            false,
            true,
            !movementIntentSent,
            authorityIsInReach,
            localPosition);
    }
}
