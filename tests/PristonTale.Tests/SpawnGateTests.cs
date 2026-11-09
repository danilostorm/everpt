using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SpawnGateTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("spawn proximity matches original connect distance and hitches do not create catch-up bursts", GatesAsync);
    }

    private static Task GatesAsync()
    {
        var field = new FieldInstance(3);
        var character = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Spawn probe");
        field.Add(new WorldPlayer(1, Vector3.Zero, character, false, default));
        var proximity = (IPlayerProximity)field;
        Assert.True(proximity.AnyPlayerNear(new Vector3(1086, 0, 0)), "near original spawn point stayed dormant");
        Assert.True(!proximity.AnyPlayerNear(new Vector3(1087, 0, 0)), "spawn gate still uses oversized guessed radius");
        Assert.True(!proximity.AnyPlayerNear(new Vector3(768, 0, 768)), "strict original squared-distance boundary became inclusive");

        var monster = new MonsterDefinition(1, "Spawn fixture", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(1, 1, new MonsterRange(1, 1), 1, 30, 0, 0, 0, 100, ResistanceBlock.Zero), "");
        var data = new FieldSpawnData([new SpawnPoint(0, 0, true)], [new SpawnTableEntry(1, 1)], [], [])
        {
            MaxConcurrentMonsters = 100,
            MaxPerSpawnPoint = 100,
            SpawnPassIntervalMs = 1800
        };
        var spawner = new FieldSpawner(data, new MonsterCatalog([monster]), new CombatRng(1));
        var sink = new CountingSink();
        spawner.MaintainPopulation(10000, [], proximity, 10000, sink);
        var firstCount = sink.Count;
        Assert.True(firstCount > 0, "due spawn pass did not run");
        for (var tick = 0; tick < 10; tick++)
        {
            spawner.MaintainPopulation(50, [], proximity, 10050 + tick * 50, sink);
        }

        Assert.True(sink.Count == firstCount, "hitch caused a spawn pass every following tick");
        spawner.MaintainPopulation(300, [], proximity, 10800, sink);
        Assert.True(sink.Count > firstCount, "spawn cadence did not resume");
        return Task.CompletedTask;
    }

    private sealed class CountingSink : ISpawnSink
    {
        public int Count { get; private set; }

        public MonsterInstance Spawn(MonsterDefinition definition, Vector3 position, int spawnPointIndex, float facing)
        {
            Count++;
            return new MonsterInstance((uint)Count, definition, position, facing, spawnPointIndex);
        }
    }
}
