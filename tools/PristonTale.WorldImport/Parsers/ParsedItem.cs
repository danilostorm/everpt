using PristonTale.Simulation.Items;

namespace PristonTale.WorldImport.Parsers;

// The importer DTO produced by ItemTxtParser for one OpenItem .txt: the parsed scalar fields plus the variadic
// **특화랜덤 class list and ** specialization bonuses. The Importer writes it as an item_definitions row and its children.
public sealed class ParsedItem
{
    public required string Code { get; init; }     // 5-char item code, the natural PK
    public required string Name { get; init; }
    public ItemKind Kind { get; init; }
    public long UniqueItem { get; init; }
    public int MaxStack { get; init; } = 1;
    public int PotionSpace { get; init; }
    // The magic mastery range (the second value is a later release's upper bound; 0 when absent).
    public float MagicMasteryMin { get; init; }
    public float MagicMasteryMax { get; init; }

    public int DurabilityMin { get; init; }
    public int DurabilityMax { get; init; }
    public int Weight { get; init; }
    public int Price { get; init; }

    // damage[4] = minLow, minHigh, maxLow, maxHigh.
    public int DamageMinLow { get; init; }
    public int DamageMinHigh { get; init; }
    public int DamageMaxLow { get; init; }
    public int DamageMaxHigh { get; init; }

    public int AttackRatingMin { get; init; }
    public int AttackRatingMax { get; init; }
    public int CriticalHit { get; init; }
    public int DefenceMin { get; init; }
    public int DefenceMax { get; init; }
    public float BlockMin { get; init; }
    public float BlockMax { get; init; }
    public float AbsorbMin { get; init; }
    public float AbsorbMax { get; init; }
    public float SpeedMin { get; init; }
    public float SpeedMax { get; init; }

    public float ManaRegenMin { get; init; }
    public float ManaRegenMax { get; init; }
    public float LifeRegenMin { get; init; }
    public float LifeRegenMax { get; init; }
    public float StaminaRegenMin { get; init; }
    public float StaminaRegenMax { get; init; }

    public int AddManaMin { get; init; }
    public int AddManaMax { get; init; }
    public int AddLifeMin { get; init; }
    public int AddLifeMax { get; init; }
    public int AddStaminaMin { get; init; }
    public int AddStaminaMax { get; init; }

    public int ReqLevel { get; init; }
    public int ReqStrength { get; init; }
    public int ReqSpirit { get; init; }
    public int ReqTalent { get; init; }
    public int ReqDexterity { get; init; }
    public int ReqHealth { get; init; }

    public int RestoreStaminaMin { get; init; }
    public int RestoreStaminaMax { get; init; }
    public int RestoreManaMin { get; init; }
    public int RestoreManaMax { get; init; }
    public int RestoreLifeMin { get; init; }
    public int RestoreLifeMax { get; init; }

    public int ShootingRange { get; init; }
    public int AttackSpeed { get; init; }
    public long JobMask { get; init; }

    // Eight resistance min/max pairs in ResistanceBlock order (bionic..wind).
    public int[] ResMin { get; } = new int[8];
    public int[] ResMax { get; } = new int[8];

    // *유니크색상: the glow of a unique weapon (signed r g b a offsets), its pulse (2^blink ms each way) and its scale.
    public int[] UniqueColor { get; } = new int[4];
    public int UniqueBlink { get; init; }
    public float UniqueScale { get; init; }
    // *이펙트설정: the weapon's effect setting; *발생제한: drops of the item per day (0 unlimited).
    public int DisplayEffect { get; init; }
    public int DailyDropLimit { get; init; }
    // *세트아이템: set code and pieces worn for the bonus, with the bonus as the file states it.
    public int SetCode { get; init; }
    public int SetPieces { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public string SetBonus { get; init; } = "";

    // Keys of the file no rule reads; the importer reports them.
    public List<string> UnknownKeys { get; } = [];

    // Variadic children.
    public List<long> JobRandom { get; } = [];                  // **특화랜덤 job bitcodes
    public List<(ItemSpecBonusKind Bonus, float Min, float Max)> Specials { get; } = []; // ** specialization bonuses
}
