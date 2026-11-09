namespace PristonTale.Simulation.World.Fields;

public sealed record SpawnPoint(int X, int Z, bool Active);

public sealed record SpawnTableEntry(int MonsterCode, int Weight);

public sealed record BossSpawn(
    int BossMonsterCode,
    int EscortMonsterCode,
    int EscortCount,
    IReadOnlyList<int> SpawnHours);

public sealed record FixedNpc(int NpcCode, int X, int Z, int Angle, int Y = 0);

public sealed record FieldSpawnData(
    IReadOnlyList<SpawnPoint> SpawnPoints,
    IReadOnlyList<SpawnTableEntry> SpawnTable,
    IReadOnlyList<BossSpawn> BossSpawns,
    IReadOnlyList<FixedNpc> FixedNpcs)
{
    public static readonly FieldSpawnData Empty = new([], [], [], []);

    public int MaxConcurrentMonsters { get; init; } = 10;

    public int MaxPerSpawnPoint { get; init; } = 3;

    public int? SpawnIntervalMask { get; init; }

    public int PerPointCooldownMs { get; init; }

    public int SpawnPassIntervalMs { get; init; } = 1800;

    public int IdleDespawnMs { get; init; } = 15000;

    public int BossIdleDespawnFactor { get; init; } = 32;
}
