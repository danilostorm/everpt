using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Loot;

namespace PristonTale.Simulation.World.Monsters;

public enum MonsterFaction
{
    Npc = 0,
    Enemy = 1
}

public enum MonsterBrood
{
    Normal = 0x00,
    Undead = 0x90,
    Mutant = 0x91,
    Demon = 0x92,
    Mechanic = 0x93,
    User = 0xA0
}

public enum MonsterNature
{
    Natural = 0x80,
    Good = 0x81,
    Evil = 0x82
}

public enum MonsterClass
{
    Normal = 0,
    Boss = 1,
    Hammer = 200,
    Ghost = 300
}

public enum MonsterActiveHour
{
    Any = 0,
    Day = 1,
    Night = -1
}

public sealed record MonsterRange(int Min, int Max)
{
    public static readonly MonsterRange Zero = new(0, 0);
}

public sealed record MonsterCombatStats(
    int Level,
    int AttackRatingAccuracy,
    MonsterRange AttackDamage,
    int AttackSpeed,
    int MeleeRange,
    int Defence,
    int Absorption,
    int BlockChance,
    int MaxLife,
    ResistanceBlock Resistances)
{
    public int DamageStunPercent { get; init; } = 100;
}

public sealed record MonsterSkillStats(
    MonsterRange SkillDamage,
    int SkillRange,
    int SkillDistance,
    int SkillRatingPercent,
    int CurseSkillCode)
{
    public static readonly MonsterSkillStats None = new(MonsterRange.Zero, 0, 0, 0, 0);
}

public sealed record MonsterAi(
    int Iq,
    MonsterNature Nature,
    int SightRadius,
    int MoveSpeed,
    int MoveRange,
    MonsterRange GroupSize,
    MonsterActiveHour ActiveHour)
{
    public static readonly MonsterAi Default =
        new(0, MonsterNature.Natural, 0, 0, 0, MonsterRange.Zero, MonsterActiveHour.Any);
}

public sealed record MonsterDefinition(
    int Code,
    string Name,
    MonsterFaction Faction,
    MonsterClass Class,
    MonsterBrood Brood,
    MonsterCombatStats Combat,
    string ModelInxPath)
{
    public MonsterSkillStats Skill { get; init; } = MonsterSkillStats.None;

    // The monster's unique English name (the importer's identity registry); crystals and skills name monsters by it.
    public string Key { get; init; } = "";

    public MonsterAi Ai { get; init; } = MonsterAi.Default;
    public DropTable Drops { get; init; } = DropTable.Empty;

    public ulong ExpReward { get; init; }

    public float ModelScale { get; init; } = 1f;

    public int SoundCode { get; init; }

    public int ShadowSizeLevel { get; init; }

    // Authored correction of the target-frame portrait camera: height and distance in world units.
    public int PortraitOffsetY { get; init; }

    public int PortraitOffsetZ { get; init; }

    // The monster's classification code, which quests hunt by (0 when no quest counts it).
    public int QuestCode { get; init; }

    // An item handed to the credited player on every kill, before the drop rolls (0 for none).
    public uint EventItem { get; init; }

    // Potions the monster carries and the percent chance it spawns with them.
    public int PotionCount { get; init; }
    public int PotionPercent { get; init; }

    // Resistance to magic, beside the eight elements.
    public int MagicResistance { get; init; }

    // Movement type and special attack chance as authored.
    public int MoveType { get; init; }
    public int SpecialAttackPercent { get; init; }

    // Gold dropped on death on top of the drop table: GoldChance percent of the kills drop DeathGold.
    public MonsterRange DeathGold { get; init; } = MonsterRange.Zero;
    public int DeathGoldChance { get; init; }

    // The model the monster changes into during its event (the hammer event); empty for none. EventFormCode names the
    // catalog form wearing it (a normal-class copy), 0 when there is none.
    public string EventModelInxPath { get; init; } = "";
    public int EventFormCode { get; init; }

    // Event creatures: code 100 walks at night in field EventInfo (the wolverines).
    public int EventCode { get; init; }
    public int EventInfo { get; init; }

    public bool IsBoss => Class == MonsterClass.Boss;
    public bool IsHostile => Faction == MonsterFaction.Enemy;
}
