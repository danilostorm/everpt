using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;

namespace PristonTale.Tests;

internal static class TerrainMarksTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("steps find the ice floors of an ice field and the shallow water of a beach", IceAndWaterAsync);
    }

    private static Task IceAndWaterAsync()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets");
        var ice = Path.Combine(root, "Field", "Ice", "ice1.smd");
        var beach = Path.Combine(root, "Field", "Lost", "lostisland.smd");
        if (!File.Exists(ice) || !File.Exists(beach))
        {
            return Task.CompletedTask;
        }

        var frozen = new PristonCollisionWorld(new PristonSmdReader().ReadStage(ice));
        var feet = new Vector3(33386f, 0f, -33553f);
        Assert.True(frozen.TryGetFloorHeight(feet, out var floor)
            && frozen.TryGetIceFloor(feet with { Y = floor }, 10f, out var mark, out var normal)
            && Math.Abs(mark.Y - floor) < 0.5f && normal.Y > 0.9f, "the ice field's floor is not ice under a step");

        var island = new PristonCollisionWorld(new PristonSmdReader().ReadStage(beach));
        var wading = false;
        for (var x = -17760f; x <= -17560f && !wading; x += 16f)
        {
            for (var z = 2214f; z <= 2414f && !wading; z += 16f)
            {
                var shallow = new Vector3(x, 0f, z);
                wading = island.TryGetWaterHeight(shallow, out var surface)
                    && island.TryGetFloorHeight(shallow with { Y = surface + 50f }, surface + 50f, 0f, out var bottom)
                    && surface > bottom && surface < bottom + 32f;
            }
        }

        Assert.True(wading, "the beach has no shallow water over its floor");
        Assert.True(!frozen.TryGetWaterHeight(feet, out _), "the frozen field has water where it is ice");
        return Task.CompletedTask;
    }
}
