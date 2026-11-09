using PristonTale.Simulation.World.Loot;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.WorldImport.Parsers;

// Importer DTO for one monster .inf (MonsterInfParser). Mirrors the monsters
// row plus the drop child rows. Drop item references are kept as RAW 5-char code strings (resolved to
// items.code by the Importer).
public sealed class ParsedMonster
{
    // The authored identity, which the source files reference; the importer maps it to the monster's id and key.
    [System.Text.Json.Serialization.JsonIgnore]
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Model { get; init; } = "";
    public int SoundCode { get; init; }
    public float ModelScale { get; init; } = 1f;
    public int PortraitOffsetY { get; init; }
    public int PortraitOffsetZ { get; init; }
    public int Level { get; init; }
    public int AttackMin { get; init; }
    public int AttackMax { get; init; }
    public int AttackSpeed { get; init; }
    public int AttackRange { get; init; }
    public int Accuracy { get; init; }
    public int Defence { get; init; }
    public int AbsorbPct { get; init; }
    public int BlockPct { get; init; }
    public int Life { get; init; }
    public int Sight { get; init; }       // RAW reach (NOT squared)
    public int SkillDamageMin { get; init; }
    public int SkillDamageMax { get; init; }
    public int SkillDistance { get; init; }
    public int SkillRange { get; init; }
    public int SkillRatingPct { get; init; }
    public int SkillCurseCode { get; init; }
    public int MoveSpeed { get; init; }
    public int MoveRange { get; init; }
    public int Iq { get; init; }
    public int GroupMin { get; init; }
    public int GroupMax { get; init; }
    // Stored as the database's signed 64-bit integer; some bosses give more than 2^31.
    public long Exp { get; init; }
    public int ActiveHour { get; init; }  // +1 day / -1 night / 0 any
    public short Nature { get; init; }
    public short Brood { get; init; }
    public short Class { get; init; }
    public int StunPct { get; init; } = 100; // *스턴율/*스턴률 flinch chance, default 100
    public int QuestCode { get; init; }       // classification code quests hunt by
    public string EventItem { get; init; } = ""; // raw item code handed out on every kill
    public int PotionCount { get; init; }
    public int PotionPercent { get; init; }

    public int[] Resistances { get; } = new int[8]; // single value per element, ResistanceBlock order
    public int MagicResistance { get; init; }   // *매직

    // *속성 적 (enemy) or anything else (a non-hostile creature); *크기 shadow size level, -1 none.
    public bool Hostile { get; init; } = true;
    public int SizeLevel { get; init; }
    public int MoveType { get; init; }
    public int SpecialAttackPct { get; init; }
    // *골드: gold dropped on death, min..max, with GoldPct percent chance.
    public int GoldMin { get; init; }
    public int GoldMax { get; init; }
    public int GoldPct { get; init; }
    // *예비모델: the model worn while its event runs.
    public string EventModel { get; init; } = "";
    // *이벤트코드 / *이벤트정보: event creatures (code 100 walk at night in field EventInfo).
    public int EventCode { get; init; }
    public int EventInfo { get; init; }

    // Keys of the file no rule reads; the importer reports them.
    public List<string> UnknownKeys { get; } = [];

    // Drop settings, the *아이템 lines in authored order and the bonus drops.
    public bool AllPlayersSee { get; set; }
    public int MaxItemsPerKill { get; set; }
    public List<ParsedDropLine> DropLines { get; } = [];
    public List<ParsedDropPlus> DropsPlus { get; } = [];
}

// One main *아이템 line: its weight and nothing, gold (GoldMin..GoldMax) or the raw codes of its items.
public sealed record ParsedDropLine(DropKind Kind, int Weight, int GoldMin, int GoldMax, IReadOnlyList<string> Codes);

// One *추가아이템 independently-rolled bonus drop (chance percent + one item code).
public sealed record ParsedDropPlus(int Chance, string? ItemCode);
