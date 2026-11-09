using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Field scenarios on a synthetic stage: flat floor plus vertical walls, stepped at the 20 Hz authoritative tick.
internal static class MonsterNavigationTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("monster leashed home behind a wall re-acquires and attacks an adjacent player", LeashBehindWallAsync);
        yield return ("monster chasing a player behind a wall slides around it instead of oscillating", SlideAroundWallAsync);
        yield return ("monster beside a still player chains attacks without walk blips or moonwalking", AttackStandAsync);
        yield return ("five monsters converging on one player never moonwalk or flee as stuck", JamAsync);
        yield return ("monsters without attack range hold position and never swing", ZeroRangeAsync);
        yield return ("monster stepping probes slide off oblique walls, climb 20 units and respect body width", SlideStepAsync);
    }

    internal static MonsterDefinition Definition(int range = 60, int sight = 400) => new(
        1, "Navigation fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
        new MonsterCombatStats(1, 1000, new MonsterRange(1, 1), 6, range, 0, 0, 0, 1000000, ResistanceBlock.Zero), "")
    {
        Ai = new MonsterAi(8, MonsterNature.Evil, sight, 1, 0, MonsterRange.Zero, MonsterActiveHour.Any)
    };

    // A floor from -3000 to 3000 and 300-unit-high walls given as (x0, z0, x1, z1) segments.
    internal static FieldTerrain Terrain(params (float X0, float Z0, float X1, float Z1)[] walls)
    {
        var vertices = new List<Vector3>
        {
            new(-3000, 0, -3000),
            new(3000, 0, -3000),
            new(3000, 0, 3000),
            new(-3000, 0, 3000)
        };
        var faces = new List<(int, int, int)> { (0, 1, 2), (0, 2, 3) };
        foreach (var (x0, z0, x1, z1) in walls)
        {
            var first = vertices.Count;
            vertices.Add(new Vector3(x0, -10, z0));
            vertices.Add(new Vector3(x1, -10, z1));
            vertices.Add(new Vector3(x1, 300, z1));
            vertices.Add(new Vector3(x0, 300, z0));
            faces.Add((first, first + 1, first + 2));
            faces.Add((first, first + 2, first + 3));
        }

        return new FieldTerrain(StageCollisionReader.Read(TerrainTests.Stage(vertices.ToArray(), faces.ToArray())));
    }

    private static Task LeashBehindWallAsync()
    {
        var scenario = new Scenario(Terrain((-400, 700, 400, 700)), Definition());
        var monster = scenario.Spawn(Vector3.Zero);
        monster.Position = new Vector3(0, 0, 1300);
        monster.TargetId = scenario.Player.EntityId;
        monster.AiState = MonsterAiState.Chase;
        monster.Motion = MotionState.Run;
        scenario.Player.Teleport(new Vector3(0, 0, 1600));
        scenario.Run(400);

        var stuck = monster.Position;
        Assert.True(stuck.Z > 690 && stuck.Z < 900, $"fixture did not bring the monster back against the wall: {stuck}");
        scenario.Player.Teleport(stuck + new Vector3(0, 0, 50));
        scenario.ResetCounters();
        scenario.Run(600);
        Assert.True(scenario.Swings > 0, $"leashed monster ignored a player 50 units away: state={monster.AiState}, position={monster.Position}");
        Assert.True(scenario.Moonwalks == 0, $"monster showed walk or run without moving on {scenario.Moonwalks} ticks");

        scenario.Player.Teleport(monster.Position + new Vector3(0, 0, 1000));
        scenario.Run(600);
        Assert.True(monster.AiState != MonsterAiState.Leash, "monster stayed in its leash return forever");
        Assert.True(scenario.Moonwalks == 0, $"monster walked in place after losing its target on {scenario.Moonwalks} ticks");
        return Task.CompletedTask;
    }

    private static Task SlideAroundWallAsync()
    {
        var scenario = new Scenario(Terrain((-400, 100, 200, 100)), Definition());
        var monster = scenario.Spawn(Vector3.Zero);
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        scenario.Player.Teleport(new Vector3(300, 0, 220));
        scenario.Run(400);
        Assert.True(scenario.Swings > 0, $"monster never got around the wall: state={monster.AiState}, position={monster.Position}");
        Assert.True(scenario.Moonwalks == 0, $"monster ran in place against the wall on {scenario.Moonwalks} ticks");
        Assert.True(scenario.LeashTicks == 0, "monster gave up the chase although sliding could reach the player");
        return Task.CompletedTask;
    }

    private static Task AttackStandAsync()
    {
        var scenario = new Scenario(FieldTerrain.None, Definition());
        var monster = scenario.Spawn(new Vector3(200, 0, 0));
        monster.AiState = MonsterAiState.Idle;
        monster.Motion = MotionState.Stand;
        scenario.Run(100);
        Assert.True(scenario.Swings > 0, "fixture monster never reached the player");
        var engaged = monster.Position;
        Assert.True(engaged.Length() < 60 && engaged.Length() > 40, $"monster did not stop short of the player inside its reach: {engaged.Length()}");
        scenario.ResetCounters();
        scenario.Run(600);
        Assert.True(scenario.Swings >= 20, $"attacks stopped chaining: {scenario.Swings}");
        Assert.True(scenario.LocomotionTicks == 0, $"monster published walk or run between attacks on {scenario.LocomotionTicks} ticks");
        Assert.True(monster.Position == engaged, "monster drifted while attacking a still player");
        return Task.CompletedTask;
    }

    private static Task JamAsync()
    {
        foreach (var staggered in new[] { true, false })
        {
            var scenario = new Scenario(FieldTerrain.None, Definition());
            var monsters = new List<MonsterInstance>();
            for (var index = 0; index < 5; index++)
            {
                var offset = staggered ? (index % 2 == 0 ? 1 : -1) * 12f * index : 0f;
                var monster = scenario.Spawn(new Vector3(150 + index * 40, 0, offset));
                monster.AiState = MonsterAiState.Idle;
                monster.Motion = MotionState.Stand;
                monsters.Add(monster);
            }

            scenario.Run(40);
            scenario.ResetCounters();
            scenario.Run(600);
            Assert.True(scenario.Moonwalks == 0, $"jammed monsters ran in place on {scenario.Moonwalks} monster-ticks");
            Assert.True(scenario.LeashTicks == 0, "a jammed monster turned back as if stuck on terrain");
            for (var left = 0; left < monsters.Count; left++)
            {
                for (var right = left + 1; right < monsters.Count; right++)
                {
                    var gap = Vector3.Distance(monsters[left].Position, monsters[right].Position);
                    Assert.True(gap > 30f, $"monsters overlapped: {gap}");
                }
            }

            if (staggered)
            {
                var attackers = monsters.Count(monster => scenario.SwingsBy(monster.EntityId) > 0);
                Assert.True(attackers >= 2, $"no monster slid around the first attacker: {attackers} attackers");
            }
        }

        return Task.CompletedTask;
    }

    private static Task ZeroRangeAsync()
    {
        var scenario = new Scenario(FieldTerrain.None, Definition(range: 0));
        var monster = scenario.Spawn(new Vector3(200, 0, 0));
        var spawn = monster.Position;
        scenario.Run(400);
        Assert.True(scenario.Swings == 0, "a monster without Shooting_Range attacked");
        Assert.True(monster.Position == spawn, $"a monster without attack range chased: {monster.Position}");
        Assert.True(scenario.LocomotionTicks == 0, "a monster without attack range published locomotion");
        return Task.CompletedTask;
    }

    private static Task SlideStepAsync()
    {
        var terrain = Terrain((-200, 100, 200, 100));
        var forward = 0f;
        Assert.True(MonsterStepping.Step(terrain, new Vector3(0, 0, 70), forward, 7f, null, out var open) == MonsterStepResult.Moved
            && open == new Vector3(0, 0, 77), $"an open step did not move forward: {open}");
        Assert.True(MonsterStepping.Step(terrain, new Vector3(0, 0, 88), forward, 7f, null, out var headOn) == MonsterStepResult.Blocked
            && headOn == new Vector3(0, 0, 88), "a head-on wall did not block all three probes");
        var oblique = MathF.PI / 4f;
        var result = MonsterStepping.Step(terrain, new Vector3(0, 0, 90), oblique, 8f, null, out var slid);
        Assert.True(result == MonsterStepResult.Slid && slid.Z < 90f && slid.X > 3.5f, $"an oblique wall did not slide half a step sideways: {result} {slid}");

        var stairs = new FieldTerrain(StageCollisionReader.Read(TerrainTests.Stage(
            [new(-100, 0, -100), new(100, 0, -100), new(100, 0, 10), new(-100, 0, 10),
             new(-100, 18, 10), new(100, 18, 10), new(100, 18, 100), new(-100, 18, 100),
             new(-100, 43, 100), new(100, 43, 100), new(100, 43, 200), new(-100, 43, 200)],
            [(0, 1, 2), (0, 2, 3), (4, 5, 6), (4, 6, 7), (8, 9, 10), (8, 10, 11)])));
        Assert.True(MonsterStepping.Step(stairs, new Vector3(0, 0, 5), forward, 10f, null, out var climbed) == MonsterStepResult.Moved
            && climbed.Y == 18f, $"an 18-unit step was refused for a server-moved monster: {climbed}");
        Assert.True(MonsterStepping.Step(stairs, new Vector3(0, 18, 95), forward, 10f, null, out var ledge) != MonsterStepResult.Moved
            && ledge.Y == 18f, $"a 25-unit rise was climbed in one step: {ledge}");
        Assert.True(MonsterStepping.Step(stairs, new Vector3(0, 18, 50), MathF.PI, 100f, null, out var fallen) == MonsterStepResult.Moved
            && fallen.Y == 0f, $"stepping off the landing did not reach the lower floor: {fallen}");

        var gap = Terrain((-300, 100, -6, 100), (6, 100, 300, 100));
        Assert.True(MonsterStepping.Step(gap, new Vector3(0, 0, 95), forward, 10f, null, out var squeezed) == MonsterStepResult.Blocked
            && squeezed.Z == 95f, "a monster body passed through a 12-unit gap");
        Assert.True(FieldTerrain.None.TryStep(Vector3.Zero, new Vector3(0, 0, 5), out _), "a field without geometry rejected a step");
        return Task.CompletedTask;
    }

    private sealed class Scenario
    {
        private readonly FieldInstance _field;
        private readonly MonsterDefinition _definition;
        private readonly Dictionary<ulong, Vector3> _previous = new();
        private readonly Dictionary<ulong, int> _swings = new();
        private readonly List<MonsterInstance> _monsters = new();

        public Scenario(FieldTerrain terrain, MonsterDefinition definition)
        {
            _definition = definition;
            _field = new FieldInstance(3, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(5),
                terrain: terrain);
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Navigation", 100);
            Player = new WorldPlayer(900, Vector3.Zero, character, false, default);
            _field.Add(Player);
        }

        public WorldPlayer Player { get; }
        public int Swings { get; private set; }
        public int Moonwalks { get; private set; }
        public int LocomotionTicks { get; private set; }
        public int LeashTicks { get; private set; }

        public MonsterInstance Spawn(Vector3 position)
        {
            var monster = ((ISpawnSink)_field).Spawn(_definition, position, -1, 0);
            _previous[monster.EntityId] = monster.Position;
            _monsters.Add(monster);
            return monster;
        }

        public int SwingsBy(ulong entityId) => _swings.GetValueOrDefault(entityId);

        public void ResetCounters()
        {
            Swings = 0;
            Moonwalks = 0;
            LocomotionTicks = 0;
            LeashTicks = 0;
            _swings.Clear();
        }

        public void Run(int ticks)
        {
            for (var tick = 0; tick < ticks; tick++)
            {
                _field.Step(0.05f);
                foreach (var combat in _field.SnapshotEvents() ?? [])
                {
                    if (combat.Kind == CombatEventKind.SwingBegin && _previous.ContainsKey(combat.AttackerId))
                    {
                        Swings++;
                        _swings[combat.AttackerId] = SwingsBy(combat.AttackerId) + 1;
                    }
                }

                foreach (var view in _field.BuildViews())
                {
                    if (view.Kind != EntityKind.Monster || !_previous.TryGetValue(view.EntityId, out var before))
                    {
                        continue;
                    }

                    var moved = view.Position.X != before.X || view.Position.Z != before.Z;
                    var locomotion = view.MoveState is MotionState.Walk or MotionState.Run;
                    if (locomotion)
                    {
                        LocomotionTicks++;
                    }

                    if (locomotion && !moved)
                    {
                        Moonwalks++;
                    }

                    _previous[view.EntityId] = view.Position;
                }

                LeashTicks += _monsters.Count(monster => monster.AiState == MonsterAiState.Leash);
            }
        }
    }
}
