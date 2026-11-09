using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Application.World;

public sealed class WorldData
{
    public WorldData(
        ItemCatalog items,
        MonsterCatalog monsters,
        NpcCatalog npcs,
        IReadOnlyDictionary<int, FieldSpawnData> fieldSpawns,
        MotionTimingCatalog motions)
    {
        Items = items;
        Monsters = monsters;
        Npcs = npcs;
        FieldSpawns = fieldSpawns;
        Motions = motions;
    }

    public ItemCatalog Items { get; }
    public MonsterCatalog Monsters { get; }
    public NpcCatalog Npcs { get; }

    public IReadOnlyDictionary<int, FieldSpawnData> FieldSpawns { get; }

    public MotionTimingCatalog Motions { get; }

    public static WorldData Empty { get; } = new(
        new ItemCatalog([]),
        new MonsterCatalog([]),
        new NpcCatalog([]),
        new Dictionary<int, FieldSpawnData>(),
        MotionTimingCatalog.Empty);
}
