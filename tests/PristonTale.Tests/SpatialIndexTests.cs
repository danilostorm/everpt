using System.Numerics;
using PristonTale.Simulation.Physics;

namespace PristonTale.Tests;

internal static class SpatialIndexTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("spatial broad phase matches whole-roster collision through movement and cell boundaries", CollisionAsync);
    }

    private static Task CollisionAsync()
    {
        var random = new Random(7921);
        var index = new SpatialIndex(64);
        var all = new EntityNeighbour[640];
        var selected = new List<EntityNeighbour>();
        var query = new List<SpatialEntry>();
        for (var entity = 0; entity < all.Length; entity++)
        {
            var position = new Vector3(random.Next(-500, 501), random.Next(-20, 21), random.Next(-500, 501));
            all[entity] = new EntityNeighbour(position, entity < 128 ? 16 : 32);
            index.Put((uint)entity + 1, position, entity);
        }

        for (var trial = 0; trial < 2000; trial++)
        {
            var old = new Vector3(random.Next(-550, 551), 0, random.Next(-550, 551));
            var desired = old + new Vector3(random.Next(-40, 41), 0, random.Next(-40, 41));
            var bounds = EntityCollision.QueryBounds(old, desired, 32);
            index.Query(bounds.Center, bounds.Radius, query);
            selected.Clear();
            var previousOrder = -1;
            foreach (var entry in query)
            {
                Assert.True(entry.Order > previousOrder, "query reordered candidates or duplicated an entry");
                previousOrder = entry.Order;
                selected.Add(all[entry.Order]);
            }

            var expected = EntityCollision.Resolve(old, desired, 16, all.AsSpan());
            var actual = EntityCollision.Resolve(old, desired, 16, selected);
            Assert.True(expected == actual, "spatial query omitted a blocking neighbour");
            var changed = random.Next(all.Length);
            all[changed] = new EntityNeighbour(desired, all[changed].Radius);
            index.Put((uint)changed + 1, desired);
        }

        index.Query(Vector3.Zero, float.MaxValue, query);
        Assert.True(query.Count == all.Length, "large radius lost candidates or failed to use bounded fallback");
        index.Clear();
        index.Put(1, new Vector3(-64, 0, -64), 0);
        index.Put(2, new Vector3(64, 0, 64), 1);
        index.Query(Vector3.Zero, 64, query);
        Assert.True(query.Count == 2, "negative or exact cell boundary dropped a candidate");
        return Task.CompletedTask;
    }
}
