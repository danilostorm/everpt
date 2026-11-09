namespace PristonTale.Persistence.Abstractions.WorldData;

// The spawn data of a field (fields, keyed by its stage; only stages the world catalog places carry a field code).
public sealed record FieldSpawnRecord(
    string Stage,
    int FieldCode,
    int MaxConcurrentMonsters,
    int MaxPerSpawnPoint,
    int SpawnInterval,
    int PerPointCooldownMs)
{
    public IReadOnlyList<FieldSpawnPointRecord> SpawnPoints { get; init; } = [];

    public IReadOnlyList<FieldMonsterSpawnRecord> MonsterSpawns { get; init; } = [];

    public IReadOnlyList<FieldBossRecord> Bosses { get; init; } = [];

    public IReadOnlyList<FieldNpcRecord> Npcs { get; init; } = [];
}

public sealed record FieldSpawnPointRecord(
    string Stage,
    int PointIndex,
    bool Active,
    int X,
    int Z);

public sealed record FieldMonsterSpawnRecord(
    string Stage,
    int Ordinal,
    int MonsterId,
    int Weight);

public sealed record FieldBossRecord(
    string Stage,
    int Ordinal,
    int MonsterId,
    int? EscortMonsterId,
    int EscortCount)
{
    public IReadOnlyList<int> Hours { get; init; } = [];
}

public sealed record FieldBossHourRecord(
    string Stage,
    int BossOrdinal,
    int Hour);

// A placed NPC: its position (1/256 world units) and heading (4096 per turn).
public sealed record FieldNpcRecord(
    string Stage,
    int RecordIndex,
    int NpcId,
    int X,
    int Y,
    int Z,
    int Angle);
