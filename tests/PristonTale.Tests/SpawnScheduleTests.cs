using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using System.Numerics;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SpawnScheduleTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("spawn mask matches the original 70 Hz counter across host rates and hitches", CadenceAsync);
        yield return ("boss minute is daily, global, and protected against duplicate hours and clock changes", ScheduleAsync);
        yield return ("bosses require configured hours and active anchors", AnchorsAsync);
        yield return ("world host shares boss passes across fields and does not replay them on entry", HostAsync);
    }

    private static Task CadenceAsync()
    {
        foreach (int mask in new[] { 0, 1, 5, 15, 127, 255 })
        {
            foreach (int dt in new[] { 10, 20, 50, 100 })
            {
                var cadence = new SpawnCadence(mask);
                int previousFrame = 0;
                for (int elapsed = dt; elapsed <= 10000; elapsed += dt)
                {
                    int frame = elapsed * 70 / 1000;
                    bool expected = false;
                    for (int counter = previousFrame + 1; counter <= frame; counter++)
                    {
                        expected |= (counter & mask) == 0;
                    }

                    Assert.True(cadence.Advance(dt) == expected, $"mask {mask}, step {dt}, elapsed {elapsed}");
                    previousFrame = frame;
                }
            }
        }

        var hitch = new SpawnCadence(127);
        Assert.True(hitch.Advance(10000), "hitch skipped every due opportunity");
        Assert.True(!hitch.Advance(50), "hitch retained a spawn backlog");
        Assert.True(!hitch.Advance(double.NaN), "invalid elapsed time created a pass");
        Assert.True(hitch.Advance(950), "cadence failed to resume at counter 768");
        return Task.CompletedTask;
    }

    private static Task ScheduleAsync()
    {
        var rng = new MinuteRng();
        var start = new DateTimeOffset(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(-3));
        var schedule = new BossSpawnSchedule(start, rng);
        Assert.True(schedule.Minute == 15 && rng.Draws == 1, "startup minute was not selected once");
        Assert.True(!schedule.TryBeginPass(start, TimeSpan.Zero), "startup spawned outside its minute");
        Assert.True(schedule.TryBeginPass(start.AddMinutes(15), TimeSpan.FromMinutes(15)), "scheduled pass missing");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(15.5), TimeSpan.FromMinutes(15.5)), "same minute duplicated");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(75), TimeSpan.FromMinutes(20)), "wall-clock jump bypassed monotonic guard");
        Assert.True(schedule.TryBeginPass(start.AddMinutes(75), TimeSpan.FromMinutes(26)), "next eligible hour blocked");
        Assert.True(rng.Draws == 1, "hour change rerolled daily minute");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(15), TimeSpan.FromMinutes(45)), "clock rollback respawned old hour");
        Assert.True(!schedule.TryBeginPass(start.AddMinutes(136), TimeSpan.FromMinutes(87)), "missed minute was replayed");
        Assert.True(schedule.TryBeginPass(start.AddDays(1).AddMinutes(30), TimeSpan.FromDays(1)), "new daily minute missing");
        Assert.True(rng.Draws == 2 && schedule.Minute == 30, "day change did not choose a new minute");
        Assert.True(!schedule.TryBeginPass(start, TimeSpan.FromDays(2)) && rng.Draws == 2, "date rollback rerolled schedule");
        return Task.CompletedTask;
    }

    private static Task AnchorsAsync()
    {
        var definition = new MonsterDefinition(1, "Boss fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var catalog = new MonsterCatalog([definition]);
        var data = new FieldSpawnData([new SpawnPoint(100, 200, true)], [], [new BossSpawn(1, 1, 2, [9])], []);
        var sink = new SpawnSink();
        var spawner = new FieldSpawner(data, catalog, new CombatRng(1));
        spawner.SpawnScheduledBosses(8, sink, sink);
        Assert.True(sink.Count == 0, "boss ignored hour list");
        spawner.SpawnScheduledBosses(9, sink, sink);
        Assert.True(sink.Count == 3 && sink.Anchor == new Vector3(100, 0, 200), "boss/escorts lost their active anchor");
        foreach (var invalid in new[]
        {
            data with { SpawnPoints = [] },
            data with { SpawnPoints = [new SpawnPoint(100, 200, false)] },
            data with { BossSpawns = [new BossSpawn(1, 1, 2, [])] }
        })
        {
            new FieldSpawner(invalid, catalog, new CombatRng(1)).SpawnScheduledBosses(9, sink, sink);
        }

        Assert.True(sink.Count == 3, "missing anchor or empty hour list produced a boss");
        return Task.CompletedTask;
    }

    private static async Task HostAsync()
    {
        var clock = new ManualClock();
        var monster = new MonsterDefinition(1, "Boss fixture", MonsterFaction.Enemy, MonsterClass.Boss, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var spawn = new FieldSpawnData([new SpawnPoint(100, 100, true)], [], [new BossSpawn(1, 0, 0, [9, 10])], []);
        var empty = WorldData.Empty;
        var holder = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new MonsterCatalog([monster]), empty.Npcs,
                new Dictionary<int, FieldSpawnData> { [3] = spawn, [4] = spawn, [5] = spawn }, empty.Motions)
        };
        var first = new FieldDef("first", "First", FieldState.Forest, new FieldBackdrop("", "", ""), "", []) { FieldCode = 3 };
        var catalog = new WorldCatalog([first, first with { Id = "second", FieldCode = 4 }, first with { Id = "third", FieldCode = 5 }]);
        var world = new WorldServer(holder, catalog, clock, new MinuteRng());
        var outboxes = new[] { new WorldOutbox(), new WorldOutbox(), new WorldOutbox() };
        for (int index = 0; index < 2; index++)
        {
            var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Boss clock fixture");
            var join = world.JoinAsync(index + 3, new WorldPlayer((uint)index + 1, Vector3.Zero, character, false, default), outboxes[index].Writer);
            world.Tick(.05f, 1);
            Assert.True(await join, "host fixture join failed");
        }

        AssertMonsterCount(outboxes[0], 0);
        AssertMonsterCount(outboxes[1], 0);
        clock.Advance(TimeSpan.FromMinutes(15));
        world.Tick(.05f, 2);
        AssertMonsterCount(outboxes[0], 1);
        AssertMonsterCount(outboxes[1], 1);

        var lateCharacter = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Late field fixture");
        var lateJoin = world.JoinAsync(5, new WorldPlayer(3, Vector3.Zero, lateCharacter, false, default), outboxes[2].Writer);
        world.Tick(.05f, 3);
        Assert.True(await lateJoin, "late field join failed");
        AssertMonsterCount(outboxes[0], 1);
        AssertMonsterCount(outboxes[2], 0);

        clock.Advance(TimeSpan.FromHours(1));
        world.Tick(.05f, 4);
        AssertMonsterCount(outboxes[0], 2);
        AssertMonsterCount(outboxes[1], 2);
        AssertMonsterCount(outboxes[2], 1);
    }

    private static void AssertMonsterCount(WorldOutbox outbox, int expected)
    {
        Assert.True(outbox.Reader.TryRead(out var snapshot), "host emitted no snapshot");
        Assert.True(snapshot.Entities.Count(entity => entity.Kind == EntityKind.Monster) == expected,
            $"expected {expected} scheduled monsters");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 12, 9, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => _now;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan elapsed)
        {
            _now += elapsed;
            _timestamp += elapsed.Ticks;
        }
    }

    private sealed class MinuteRng : ISimRng
    {
        public int Draws { get; private set; }

        public int NextInclusive(int minInclusive, int maxInclusive)
        {
            Assert.True(minInclusive == 1 && maxInclusive == 45, "boss minute range differs from source");
            Draws++;
            return Draws == 1 ? 15 : 30;
        }

        public int NextExclusive(int minInclusive, int maxExclusive) => throw new InvalidOperationException();
        public float NextUnit() => throw new InvalidOperationException();
    }

    private sealed class SpawnSink : ISpawnSink, IPlayerProximity
    {
        public int Count { get; private set; }
        public Vector3 Anchor { get; private set; }
        public bool AnyPlayerNear(Vector3 position) => false;

        public MonsterInstance Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
        {
            if (Count == 0)
            {
                Anchor = position;
            }

            Count++;
            return new MonsterInstance((uint)Count, definition, position, facing, spawnPointIndex);
        }
    }
}
