using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class NpcMovementTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("NPCs with an authored Walk clip wander authoritatively inside MoveRange", WanderingAsync);
        yield return ("NPCs without an authored Walk clip remain fixed", FixedAsync);
    }

    private static Task WanderingAsync()
    {
        var timings = MotionTimingSet.Build(
        [
            new KeyValuePair<int, MotionClip>((int)MotionCode.Walk, Clip(0, 30)),
            new KeyValuePair<int, MotionClip>((int)MotionCode.Stand, Clip(40, 80)),
            new KeyValuePair<int, MotionClip>((int)MotionCode.Stand, Clip(90, 150))
        ]);
        var definition = new NpcDefinition(1, "Warehouse", "char/npc/TN-010/TN-010.inx", [new NpcService(NpcServiceKind.Warehouse)])
        {
            MoveRange = 64
        };
        var field = Field(definition, timings);
        var npc = field.Npcs.Single();
        var home = npc.HomeAnchor;
        var presence = new SubscriberPresence(0);
        var initial = presence.Diff(field.BuildViews());
        var sawWalk = false;
        var sawDisplacement = false;
        var publishedWalk = false;

        Assert.True(
            initial.Spawns.Count == 1 && initial.Spawns[0].EntityId == npc.EntityId,
            "NPC was not published in the initial presence roster");

        for (var tick = 0; tick < 4_000; tick++)
        {
            field.Step(0.05f);
            sawWalk |= npc.MoveState == MotionState.Walk;
            sawDisplacement |= Vector3.DistanceSquared(npc.Position, home) > 0.01f;
            var diff = presence.Diff(field.BuildViews());
            publishedWalk |= diff.States.Any(state =>
                state.EntityId == npc.EntityId
                && state.MoveState == MotionState.Walk
                && Vector3.DistanceSquared(state.Position, home) > 0.01f);
            Assert.True(Vector3.Distance(npc.Position, home) <= definition.MoveRange + 0.01f, "NPC escaped its authored movement radius");
        }

        Assert.True(npc.CanWalk && sawWalk && sawDisplacement, "Walk-capable NPC never entered authoritative movement");
        Assert.True(publishedWalk, "authoritative NPC movement never reached the presence diff");
        Assert.True(npc.ToView().MoveSpeed == 1, "NPC did not expose the original baseline animation rate");
        return Task.CompletedTask;
    }

    private static Task FixedAsync()
    {
        var timings = MotionTimingSet.Build(
        [
            new KeyValuePair<int, MotionClip>((int)MotionCode.Stand, Clip(0, 40))
        ]);
        var definition = new NpcDefinition(1, "Smith", "char/npc/TN-003/TN-003.inx", [new NpcService(NpcServiceKind.Shop)]);
        var field = Field(definition, timings);
        var npc = field.Npcs.Single();
        var original = npc.Position;

        for (var tick = 0; tick < 1_000; tick++)
        {
            field.Step(0.05f);
        }

        Assert.True(!npc.CanWalk && npc.Position == original, "Stand-only NPC moved without an INX Walk clip");
        Assert.True(npc.ToView().MoveState == MotionState.Stand && npc.ToView().MoveSpeed == 0, "fixed NPC advertised locomotion");
        return Task.CompletedTask;
    }

    private static FieldInstance Field(NpcDefinition definition, MotionTimingSet timings)
    {
        var spawn = FieldSpawnData.Empty with
        {
            FixedNpcs = [new FixedNpc(definition.Code, 100 * 256, 200 * 256, 1024)]
        };
        var motions = new MotionTimingCatalog(
            new Dictionary<string, MotionTimingSet>
            {
                [MotionTimingCatalog.NormalizeKey(definition.ModelInxPath)] = timings
            });
        return new FieldInstance(
            3,
            spawn,
            new MonsterCatalog([]),
            new EntityIdAllocator(),
            new CombatRng(3),
            motions,
            new NpcCatalog([definition]));
    }

    private static MotionClip Clip(int start, int end)
    {
        return new MotionClip(start, end, 0, 0, 0, 0, true);
    }
}
