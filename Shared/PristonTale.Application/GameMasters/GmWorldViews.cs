using System.Numerics;

namespace PristonTale.Application.GameMasters;

// Where a player stands, as a game-master command reads it from the world.
public readonly record struct GmPlacement(int FieldId, Vector3 Position);

// A running force orb: its icon number, the seconds it has left and the damage it adds.
public readonly record struct GmForceOrb(int Icon, int Seconds, int Damage);

// What the information commands read from one character on the tick.
public sealed record GmCharacterReport(
    int FieldId,
    Vector3 Position,
    long Money,
    GmForceOrb? ForceOrb,
    long ThirdEyesSeconds,
    long ExperienceUpSeconds,
    int DamageMin,
    int DamageMax,
    int Critical,
    // Carried potions by kind (life, mana, stamina) and grade (1 to 4), as counted from the inventory.
    int[,] Potions);

public readonly record struct GmSpawnResult(bool Known, int Spawned);

public readonly record struct GmFieldMonsters(int FieldId, int ActiveSpawnPoints, int SpawnPointMonsters, int Monsters, int LiveMonsters, int IntervalSeconds);

public readonly record struct GmEventMonster(string Name, long SecondsLeft, int Permille);

public readonly record struct GmAbsorption(string Name, int Before, int After);
