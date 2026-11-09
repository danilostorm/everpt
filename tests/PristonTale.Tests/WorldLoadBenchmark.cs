using System.Diagnostics;
using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class WorldLoadBenchmark
{
    public static void Run()
    {
        Console.WriteLine($"Field CPU workload: 128 players + 512 monsters; 20 Hz; Release; runtime={Environment.Version}; CPUs={Environment.ProcessorCount}");
        Console.WriteLine("Includes AI, combat, collision, snapshots and 128 real WorldOutboxes drained synchronously; excludes GPU, sockets and database.");
        foreach (var spacing in new[] { 128, 64 })
        {
            for (var run = 0; run < 5; run++)
            {
                Measure(spacing, run);
            }
        }
    }

    private static void Measure(int spacing, int run)
    {
        var definition = new MonsterDefinition(1, "Load fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(40, 100, new MonsterRange(0, 0), 6, 50, 10, 0, 0, 1000000, ResistanceBlock.Zero), "")
        {
            Ai = new MonsterAi(5, MonsterNature.Evil, 600, 3, 1024, new MonsterRange(1, 1), MonsterActiveHour.Any)
        };
        var field = new FieldInstance(0, FieldSpawnData.Empty, new MonsterCatalog([definition]), new EntityIdAllocator(), new CombatRng(37));
        var sink = (ISpawnSink)field;
        var monsters = new ulong[512];
        for (var index = 0; index < monsters.Length; index++)
        {
            monsters[index] = sink.Spawn(definition, new Vector3(index % 32 * spacing + 48, 0, index / 32 * spacing + 48), -1, 0).EntityId;
        }

        var classes = new CharacterClassRegistry();
        var players = new WorldPlayer[128];
        var outboxes = new WorldOutbox[128];
        var owners = new OwnerState?[128];
        for (var index = 0; index < players.Length; index++)
        {
            var character = classes.CreatePlayer(PlayerClass.Fighter, "Load", 40);
            var player = new WorldPlayer((uint)index + 1, new Vector3(index % 32 * spacing, 0, index / 32 * spacing * 4), character, false, default);
            field.Add(player);
            field.SetAttackTarget(player.EntityId, monsters[index * 4], true);
            players[index] = player;
            outboxes[index] = new WorldOutbox();
        }

        var durations = new double[1000];
        var eventCount = 0;
        for (uint tick = 0; tick < 250; tick++)
        {
            Step(field, players, owners, outboxes, tick);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (uint tick = 0; tick < durations.Length; tick++)
        {
            var start = Stopwatch.GetTimestamp();
            eventCount += Step(field, players, owners, outboxes, tick + 250);
            durations[tick] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Array.Sort(durations);
        Assert.True(field.PlayerCount == 128 && field.MonsterCount == 512, "benchmark population changed");
        Console.WriteLine($"spacing={spacing} run={run + 1} median={durations[500]:F3}ms p95={durations[950]:F3}ms p99={durations[990]:F3}ms "
            + $"max={durations[^1]:F3}ms bytes/tick={allocated / durations.Length} events={eventCount}");
    }

    private static int Step(FieldInstance field, WorldPlayer[] players, OwnerState?[] owners, WorldOutbox[] outboxes, uint tick)
    {
        field.Step(0.05f);
        var entities = field.BuildViews();
        var events = field.SnapshotEvents() ?? Array.Empty<CombatEventView>();
        for (var index = 0; index < players.Length; index++)
        {
            owners[index] = OwnerState.Capture(players[index].Character, owners[index]);
            Assert.True(outboxes[index].Writer.TryWrite(new WorldSnapshot(tick, entities, events, field.FieldId, owners[index])), "outbox rejected benchmark traffic");
            Assert.True(outboxes[index].Reader.TryRead(out _), "outbox did not deliver the latest state");
        }

        return events.Length;
    }
}
