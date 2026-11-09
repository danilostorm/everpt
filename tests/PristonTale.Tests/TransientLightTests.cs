using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Rendering;
using PristonTale.Engine3D.Rendering.Lighting;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

internal static class TransientLightTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("original combat lights preserve power, radius, decay and bounded selection", PoolAsync);
        yield return ("transient lights advance without FPS dependence or frame allocations", FramesAsync);
        yield return ("Dark Mage meteor preserves original offsets and speed", MeteorAsync);
    }

    private static Task PoolAsync()
    {
        var pool = new PristonTransientLights();
        var selected = new List<PristonPointLight>(16);
        pool.Add(Vector3.Zero, new Color(100, 255, 100), 100, 1);
        pool.Select(Vector3.Zero, selected);
        Assert.Near(64, selected[0].Radius);
        Assert.Near(39f / 255, selected[0].Color.X);
        Assert.Near(99f / 255, selected[0].Color.Y);
        pool.Update(.5f);
        pool.Select(Vector3.Zero, selected);
        Assert.Near(64f / 255, selected[0].Color.Y);
        pool.Update(2);
        pool.Select(Vector3.Zero, selected);
        Assert.True(pool.Count == 0 && selected.Count == 0, "expired light remained selected");
        for (int index = 0; index < PristonTransientLights.Capacity; index++)
        {
            Assert.True(pool.Add(new Vector3(index, 0, 0), Color.White, 250, 2), "pool lost a free slot");
        }

        Assert.True(!pool.Add(Vector3.Zero, Color.White, 250, 2), "light pool grew beyond its budget");
        pool.Select(Vector3.Zero, selected);
        Assert.True(selected.Count == PristonTransientLights.ShaderBudget, "shader budget was exceeded");
        for (int index = 0; index < selected.Count; index++)
        {
            Assert.Near(index, selected[index].Position.X);
        }

        pool.Clear();
        Assert.True(pool.Add(Vector3.Zero, Color.White, 250, 2), "cleared pool did not recycle slots");
        return Task.CompletedTask;
    }

    private static Task FramesAsync()
    {
        var one = new PristonTransientLights();
        var many = new PristonTransientLights();
        var selected = new List<PristonPointLight>(16);
        one.Add(Vector3.One, Color.White, 250, 2);
        many.Add(Vector3.One, Color.White, 250, 2);
        one.Update(.5f);
        for (int index = 0; index < 50; index++)
        {
            many.Update(.01f);
        }

        one.Select(Vector3.Zero, selected);
        var expected = selected[0];
        many.Select(Vector3.Zero, selected);
        Assert.True(expected == selected[0], "light decay changed with frame rate");
        for (int index = 0; index < 100; index++)
        {
            many.Select(Vector3.Zero, selected);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            many.Select(Vector3.Zero, selected);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() == before, "light selection allocates per frame");
        return Task.CompletedTask;
    }

    private static Task MeteorAsync()
    {
        var target = new Vector3(10, 20, 30);
        var trajectory = MeteorEffects.Trajectory(target);
        Assert.Near(10, trajectory.Start.X);
        Assert.Near(20 + 130000f / 256, trajectory.Start.Y);
        Assert.Near(30 - 51000f / 256, trajectory.Start.Z);
        Assert.Near(560, trajectory.Velocity.Length());
        float flightTime = (trajectory.Start.Y - target.Y) / -trajectory.Velocity.Y;
        var contact = trajectory.Start + trajectory.Velocity * flightTime;
        Assert.Near(target.Z - 1000f / 256, contact.Z);
        return Task.CompletedTask;
    }
}
