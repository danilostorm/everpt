using Microsoft.Xna.Framework;
using PristonTale.Simulation.Combat;

namespace PristonTale.Gameplay.World.Remote;

public static class AttackPresentation
{
    public const float MeleeRange = MeleeReach.PlayerBaseRange;

    public enum Action
    {
        Approach,
        Attack,
    }

    public readonly record struct Decision(Action Action, Vector2 ApproachDirection)
    {
        public bool ShouldAttack => Action == Action.Attack;
    }

    public static Decision Decide(Vector2 playerPlanar, Vector2 targetPlanar, float attackRange = MeleeRange)
    {
        var toTarget = targetPlanar - playerPlanar;
        var distSq = toTarget.LengthSquared();

        if (distSq <= 1e-6f)
        {
            return new Decision(Action.Attack, Vector2.Zero);
        }

        var dir = toTarget / MathF.Sqrt(distSq);
        return distSq <= attackRange * attackRange
            ? new Decision(Action.Attack, Vector2.Zero)
            : new Decision(Action.Approach, dir);
    }

    // The forward that looks from the player toward a target (a swing's, a cast's), whatever the range; a target on the
    // player's own spot keeps the current forward.
    public static Vector2 FacingToward(Vector2 playerPlanar, Vector2 targetPlanar, Vector2 current)
    {
        var toTarget = targetPlanar - playerPlanar;
        return toTarget.LengthSquared() > 1e-6f ? Vector2.Normalize(toTarget) : current;
    }
}
