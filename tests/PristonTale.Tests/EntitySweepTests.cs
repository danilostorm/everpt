using System.Numerics;
using PristonTale.Simulation.Physics;

namespace PristonTale.Tests;

internal static class EntitySweepTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("entity collision blocks tunnelling and preserves escape, tangency and vertical separation", SweepAsync);
    }

    private static Task SweepAsync()
    {
        var blockers = new List<EntityNeighbour> { new(new Vector3(50, 0, 0), 32) };
        var origin = Vector3.Zero;
        var destination = new Vector3(100, 0, 0);
        Assert.True(EntityCollision.Resolve(origin, destination, 16, blockers) == origin, "long frame tunneled through the blocker");
        Assert.True(EntityCollision.Resolve(new Vector3(50, 0, 0), destination, 16, blockers) == destination, "existing overlap could not escape");
        var highOrigin = new Vector3(0, 16, 0);
        var highDestination = new Vector3(100, 16, 0);
        Assert.True(EntityCollision.Resolve(highOrigin, highDestination, 16, blockers) == highDestination, "vertical boundary was inflated");
        var tangentOrigin = new Vector3(0, 0, 16);
        var tangentDestination = new Vector3(100, 0, 16);
        Assert.True(EntityCollision.Resolve(tangentOrigin, tangentDestination, 16, blockers) == tangentDestination, "tangent path was blocked");
        var above = new Vector3(50, 100, 0);
        var below = new Vector3(50, -100, 0);
        Assert.True(EntityCollision.Resolve(above, below, 16, blockers) == above, "vertical segment skipped the collision band");
        foreach (var hz in new[] { 30, 60, 120, 144 })
        {
            var position = origin;
            for (var frame = 0; frame < hz; frame++)
            {
                var desired = position + new Vector3(120f / hz, 0, 0);
                position = EntityCollision.Resolve(position, desired, 16, blockers);
            }

            Assert.True(position.X <= 34 && position.X >= 30, "cadence changed which side of the blocker the player reached");
        }

        var bounds = EntityCollision.QueryBounds(origin, destination, 32);
        Assert.True(bounds.Center == new Vector3(50, 0, 0) && bounds.Radius == 82, "broad phase does not cover the full move");
        return Task.CompletedTask;
    }
}
