using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Contracts.Motion;

namespace PristonTale.Tests;

internal static class MonsterMovementTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("monster pursuit and attacks face forward in all eight directions", HeadingAsync);
        yield return ("monster translation uses original generic and family movement steps", SpeedAsync);
        yield return ("untargeted monsters walk two clips then stand without orbiting home", SpawnWalkAsync);
        yield return ("monster stuck detection measures five clips, then turns off the way home for ten keeping its target", StuckAsync);
        yield return ("monster leaving its leash heads home for six clips and may resume the chase", SoftLeashAsync);
        yield return ("monster perception includes height and population leash is independent of NPC movement range", PerceptionAsync);
        yield return ("summons and clever monsters without a run clip walk at walking speed", NoRunClipAsync);
    }

    private static MonsterDefinition Definition() => new(
        1, "Movement fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(1, 1000, new MonsterRange(10, 10), 6, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(6, MonsterNature.Evil, 2000, 1, 64, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    private static Task SpeedAsync()
    {
        var definition = Definition();
        Assert.Near(70, MonsterLocomotion.UnitsPerSecond(definition, false));
        Assert.Near(140, MonsterLocomotion.UnitsPerSecond(definition, true));
        Assert.Near(140, MonsterLocomotion.UnitsPerSecond(definition with { Ai = definition.Ai with { MoveSpeed = 9 } }, true));
        Assert.Near(350, MonsterLocomotion.UnitsPerSecond(definition with { SoundCode = 0x11A0 }, true));
        Assert.Near(210, MonsterLocomotion.UnitsPerSecond(definition with { SoundCode = 0x11E0 }, true));
        var darkKnight = definition with { Class = MonsterClass.Boss, SoundCode = 0x1460 };
        Assert.Near(140, MonsterLocomotion.UnitsPerSecond(darkKnight, false));
        Assert.Near(210, MonsterLocomotion.UnitsPerSecond(darkKnight, true));
        return Task.CompletedTask;
    }

    private static Task NoRunClipAsync()
    {
        var definition = Definition();
        var walkOnly = new MonsterAiTuning(definition, MotionTimingSet.Build([new((int)MotionCode.Walk, new MotionClip(10, 30, 0, 0, 0, 0, true))]));
        var both = new MonsterAiTuning(definition, MotionTimingSet.Build([
            new((int)MotionCode.Walk, new MotionClip(10, 30, 0, 0, 0, 0, true)),
            new((int)MotionCode.Run, new MotionClip(40, 60, 0, 0, 0, 0, true))
        ]));
        Assert.True(!walkOnly.HasRunClip && !walkOnly.CanRun, "a model without a run clip may run");
        Assert.True(both.HasRunClip && both.CanRun, "a clever model with a run clip may not run");
        Assert.True(!new MonsterAiTuning(definition with { Ai = definition.Ai with { Iq = 5 } }, MotionTimingSet.Build([
            new((int)MotionCode.Run, new MotionClip(40, 60, 0, 0, 0, 0, true))
        ])).CanRun, "a dull monster runs");

        var brain = new SummonBrain(new CombatRng(1));
        var orders = new SummonOrders(new Vector3(1000, 0, 0), default, false);
        var walker = new MonsterInstance(2, definition, Vector3.Zero, 0, 0);
        brain.Tick(walker, walkOnly, orders, 1000);
        Assert.True(walker.Motion == MotionState.Walk, "a summon without a run clip did not walk to its owner");
        Assert.Near(MonsterLocomotion.UnitsPerSecond(definition, false), walker.Position.X);

        var runner = new MonsterInstance(3, definition, Vector3.Zero, 0, 0);
        brain.Tick(runner, both, orders, 1000);
        Assert.True(runner.Motion == MotionState.Run, "a summon with a run clip did not run to its owner");
        Assert.Near(MonsterLocomotion.UnitsPerSecond(definition, true), runner.Position.X);
        return Task.CompletedTask;
    }

    private static Task HeadingAsync()
    {
        var definition = Definition();
        var tuning = new MonsterAiTuning(definition);
        for (int direction = 0; direction < 8; direction++)
        {
            float yaw = direction * MathF.PI / 4f;
            var forward = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));
            var prey = new Prey(new PreyCandidate(1, forward * 300, true));
            var monster = new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
            {
                TargetId = 1,
                AiState = MonsterAiState.Chase
            };
            var brain = new MonsterAiBrain(new CombatRng(1));
            brain.Tick(monster, tuning, prey, 60);
            Assert.True(Vector3.Dot(Vector3.Normalize(monster.Position), forward) > .999f, "pursuit moved off heading");
            Assert.True(Vector3.Dot(new Vector3(MathF.Sin(monster.Facing), 0, MathF.Cos(monster.Facing)), forward) > .999f,
                "monster yaw disagrees with player/presence convention");

            prey.Candidate = new PreyCandidate(1, monster.Position + forward * 10, true);
            brain.Tick(monster, tuning, prey, 60);
            Assert.True(monster.AiState == MonsterAiState.Attack, "close prey did not begin attack");
            Assert.Near(forward.X, MathF.Sin(monster.Facing));
            Assert.Near(forward.Z, MathF.Cos(monster.Facing));
        }

        return Task.CompletedTask;
    }

    private static Task SpawnWalkAsync()
    {
        var definition = Definition();
        var timing = MotionTimingSet.Build([new((int)MotionCode.Walk, new MotionClip(10, 30, 0, 0, 0, 0, true))]);
        var tuning = new MonsterAiTuning(definition, timing);
        var home = new Vector3(4500, 0, -12000);
        var monster = new MonsterInstance(2, definition, home, MathF.PI / 2, 0);
        var brain = new MonsterAiBrain(new CombatRng(1));
        var prey = new Prey();
        brain.Tick(monster, tuning, prey, 50);
        Assert.True(monster.Position.X > home.X && MathF.Abs(monster.Position.Z - home.Z) < .001f,
            "spawn drifted toward origin or turned instead of following spawn heading");
        for (int tick = 1; tick < 27; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
        }

        Assert.True(monster.Motion == MotionState.Stand, "two authored walk cycles did not stop wandering");
        var stopped = monster.Position;
        for (int tick = 0; tick < 100; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
        }

        Assert.True(monster.Position == stopped, "idle monster resumed invented random wandering");
        Assert.Near(2 * tuning.WalkCycleMs, monster.SpawnWalkElapsedMs);
        return Task.CompletedTask;
    }

    private static Task StuckAsync()
    {
        var definition = Definition();
        var timing = MotionTimingSet.Build([
            new((int)MotionCode.Walk, new MotionClip(0, 10, 0, 0, 0, 0, true)),
            new((int)MotionCode.Run, new MotionClip(0, 10, 0, 0, 0, 0, true))
        ]);
        var tuning = new MonsterAiTuning(definition, timing);
        var monster = new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
        {
            Position = new Vector3(100, 0, 0),
            ProgressProbePosition = new Vector3(100, 0, 0),
            TargetId = 1,
            AiState = MonsterAiState.Chase
        };
        var brain = new MonsterAiBrain(new CombatRng(1));
        var prey = new Prey(new PreyCandidate(1, new Vector3(800, 0, 0), true));
        for (int tick = 0; tick < 32; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
            monster.Position = new Vector3(100, 0, 0); // Host collision rejects the proposed step.
            Assert.True(monster.AiState == MonsterAiState.Chase, "stuck recovery fired before five clips");
        }

        brain.Tick(monster, tuning, prey, 60);
        Assert.True(monster.AiState == MonsterAiState.Leash && monster.TargetId == 1 && monster.Motion == MotionState.Run,
            "stuck manoeuvre dropped the target or did not run");
        Assert.True(monster.ManoeuvreClipsLeft == MonsterAiTuning.StuckManoeuvreClips, "stuck manoeuvre did not last ten clips");
        var home = -Vector3.UnitX;
        var heading = new Vector3(MathF.Sin(monster.ManoeuvreHeading), 0, MathF.Cos(monster.ManoeuvreHeading));
        Assert.True(Vector3.Dot(heading, home) >= MathF.Cos(MathF.PI / 4f) - 0.001f, "stuck turn exceeded 45 degrees off the way home");
        Assert.True(monster.Position.X < 100 && monster.Position.X > 90, "blocked monster teleported instead of stepping");
        var turned = monster.ManoeuvreHeading;
        for (int tick = 0; tick < 40; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
            Assert.True(monster.AiState == MonsterAiState.Leash && monster.ManoeuvreHeading == turned, "manoeuvre ended or turned early");
        }

        for (int tick = 0; tick < 40 && monster.AiState == MonsterAiState.Leash; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
        }

        Assert.True(monster.AiState == MonsterAiState.Chase && monster.TargetId == 1, "chase did not resume on the kept target");
        return Task.CompletedTask;
    }

    private static Task SoftLeashAsync()
    {
        var definition = Definition();
        var timing = MotionTimingSet.Build([
            new((int)MotionCode.Walk, new MotionClip(0, 10, 0, 0, 0, 0, true)),
            new((int)MotionCode.Run, new MotionClip(0, 10, 0, 0, 0, 0, true))
        ]);
        var tuning = new MonsterAiTuning(definition, timing);
        var monster = new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
        {
            Position = new Vector3(1270, 0, 0),
            ProgressProbePosition = new Vector3(1270, 0, 0),
            TargetId = 1,
            AiState = MonsterAiState.Chase
        };
        var brain = new MonsterAiBrain(new CombatRng(1));
        var prey = new Prey(new PreyCandidate(1, new Vector3(1500, 0, 0), true));
        var leashed = false;
        for (int tick = 0; tick < 20 && !leashed; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
            leashed = monster.AiState == MonsterAiState.Leash;
        }

        Assert.True(leashed && monster.TargetId == 1 && monster.ManoeuvreClipsLeft == MonsterAiTuning.LeashManoeuvreClips,
            "leaving the leash did not start a six-clip return that keeps the target");
        Assert.True(MathF.Abs(MathF.Sin(monster.ManoeuvreHeading)) > 0.999f && MathF.Sin(monster.ManoeuvreHeading) < 0, "leash did not head home");
        prey.Candidate = new PreyCandidate(1, monster.Position + new Vector3(-150, 0, 0), true);
        var resumed = false;
        for (int tick = 0; tick < 200 && !resumed; tick++)
        {
            brain.Tick(monster, tuning, prey, 50);
            resumed = monster.AiState is MonsterAiState.Chase or MonsterAiState.Attack;
        }

        Assert.True(resumed && monster.TargetId == 1, "monster never resumed the chase after its leash return");
        return Task.CompletedTask;
    }

    private static Task PerceptionAsync()
    {
        var definition = Definition() with { Ai = Definition().Ai with { SightRadius = 150 } };
        var tuning = new MonsterAiTuning(definition);
        Assert.True(tuning.LeashSq == 1280L * 1280, "population leash incorrectly uses NPC MoveRange");
        var monster = new MonsterInstance(2, definition, Vector3.Zero, 0, 0)
        {
            AiState = MonsterAiState.Idle,
            Motion = MotionState.Stand
        };
        var prey = new Prey(new PreyCandidate(1, new Vector3(120, 100, 0), true));
        new MonsterAiBrain(new CombatRng(1)).Tick(monster, tuning, prey, 900);
        Assert.True(monster.TargetId == 0, "3D sight sphere became a cylinder");
        return Task.CompletedTask;
    }

    private sealed class Prey : IPreyView
    {
        public PreyCandidate Candidate { get; set; }
        public IReadOnlyList<PreyCandidate> Candidates => Candidate.EntityId == 0 ? [] : new[] { Candidate };

        public Prey(PreyCandidate candidate = default) => Candidate = candidate;

        public bool TryGet(ulong entityId, out PreyCandidate prey)
        {
            prey = Candidate;
            return entityId != 0 && entityId == Candidate.EntityId;
        }
    }
}
