using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class PlayerShotTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a bow shoots at the attack clip's first event, flies to its target and ends 7 frames after reaching it", ArrowAsync);
        yield return ("a glowing bow sheds sparks, a javelin hides in the hand while it flies, a staff strikes for a fighter", WeaponsAsync);
    }

    private static readonly PristonMotionInfo Attack = new((uint)MotionCode.Attack, 10, 40, [800u], 0, 0, 0, 1, 0, 0);

    private static Vector3? Target(ulong id) => id == 2 ? new Vector3(0f, 0f, -200f) : null;

    private static Task ArrowAsync()
    {
        var shots = new PlayerShots();
        var hand = Matrix.CreateTranslation(0f, 30f, 0f);
        shots.ObserveSwing(1, 2);
        shots.Hold(null, 1, Attack, 1600f, "WS101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        Assert.True(shots.Flying == 0, "the arrow left before the attack's event");
        shots.Hold(null, 1, Attack, 1600f + 900f, "WS101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        Assert.True(shots.Flying == 1, "the arrow did not leave at the attack's event");
        shots.Hold(null, 1, Attack, 1600f + 1000f, "WS101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        Assert.True(shots.Flying == 1, "the same event threw twice");

        // 200 units at 8 a frame reach the target in about 18 frames; 7 more end the shot.
        for (var frame = 0; frame < 20; frame++)
        {
            shots.Hold(null, 1, Attack, 3000f, "WS101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
            shots.Update(1f / 70f, Target);
        }

        Assert.True(shots.Flying == 1, "the arrow ended before reaching its target and flying on");
        for (var frame = 0; frame < 10; frame++)
        {
            shots.Hold(null, 1, Attack, 3000f, "WS101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
            shots.Update(1f / 70f, Target);
        }

        Assert.True(shots.Flying == 0 && shots.Sparks == 0, "the arrow did not end after reaching its target, or a plain bow shed sparks");
        return Task.CompletedTask;
    }

    private static Task WeaponsAsync()
    {
        var shots = new PlayerShots();
        var hand = Matrix.CreateTranslation(0f, 30f, 0f);
        var glow = new HeldItemGlow(220, 80, 0, 0, 9, 0f, -1, 0, false);
        shots.ObserveSwing(1, 2);
        shots.Hold(null, 1, Attack, 1600f, "WS110", hand, glow, Vector3.Zero, false, Target);
        shots.Hold(null, 1, Attack, 2500f, "WS110", hand, glow, Vector3.Zero, false, Target);
        shots.Update(3f / 70f, Target);
        Assert.True(shots.Flying == 1 && shots.Sparks >= 9, $"a glowing arrow shed {shots.Sparks} sparks in three frames");

        var javelins = new PlayerShots();
        javelins.ObserveSwing(3, 2);
        javelins.Hold(null, 3, Attack, 1600f, "WT101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        javelins.Hold(null, 3, Attack, 2500f, "WT101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        Assert.True(javelins.Throwing(3) && !javelins.Throwing(1), "a thrown javelin is not marked as flying for its thrower");

        var staves = new PlayerShots();
        staves.ObserveSwing(4, 2);
        staves.Hold(null, 4, Attack, 1600f, "WM101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        staves.Hold(null, 4, Attack, 2500f, "WM101", hand, HeldItemGlow.None, Vector3.Zero, false, Target);
        Assert.True(staves.Flying == 0, "a fighter's staff threw a shot");
        return Task.CompletedTask;
    }
}
