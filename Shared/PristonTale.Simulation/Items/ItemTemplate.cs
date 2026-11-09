namespace PristonTale.Simulation.Items;

public readonly record struct ItemRange(int Min, int Max)
{
    public static readonly ItemRange None = default;

    public static ItemRange Fixed(int value) => new(value, 0);
}

public readonly record struct ItemFloatRange(float Min, float Max)
{
    public static readonly ItemFloatRange None = default;

    public static ItemFloatRange Fixed(float value) => new(value, 0);
}

// The authored value ranges of an item. A range whose Max is zero is a fixed value (its Min), as the authored item
// files write a single number; resistances keep one range per element in ResistanceBlock order.
public sealed record ItemTemplate
{
    public ItemRange Durability { get; init; }
    public ItemRange DamageMin { get; init; }
    public ItemRange DamageMax { get; init; }
    public ItemRange AttackRating { get; init; }
    public ItemRange Defence { get; init; }
    public ItemFloatRange Absorb { get; init; }
    public ItemFloatRange Block { get; init; }
    public ItemFloatRange MoveSpeed { get; init; }
    public ItemFloatRange LifeRegen { get; init; }
    public ItemFloatRange ManaRegen { get; init; }
    public ItemFloatRange StaminaRegen { get; init; }
    public ItemRange AddLife { get; init; }
    public ItemRange AddMana { get; init; }
    public ItemRange AddStamina { get; init; }
    public ItemFloatRange MagicMastery { get; init; }
    public IReadOnlyList<ItemRange> Resistances { get; init; } = new ItemRange[8];

    // The fixed class specialization, and the classes one of which a dropped copy may take instead.
    public uint SpecJob { get; init; }
    public IReadOnlyList<uint> RandomSpecJobs { get; init; } = [];

    public ItemSpecTemplate Spec { get; init; } = ItemSpecTemplate.None;

    public bool CanSpecialize => SpecJob != 0 || RandomSpecJobs.Count > 0;
}

// A class specialization bonus an item definition grants (stored as item_spec_bonuses.bonus; append only).
public enum ItemSpecBonusKind : short
{
    Absorb = 0,
    Defence = 1,
    MoveSpeed = 2,
    Block = 3,
    AttackSpeed = 4,
    Critical = 5,
    ShootingRange = 6,
    MagicMastery = 7,
    LevelMana = 8,
    LevelLife = 9,
    LevelAttackRating = 10,
    // The authored damage per level pair; the second value is the divisor.
    LevelDamage = 11,
    ManaRegen = 12,
    LifeRegen = 13,
    StaminaRegen = 14,
    LifeRegenRange = 15
}

// Bonuses a specialized copy grants to a character of its class. Level bonuses are divisors: the character gains
// Level / divisor. Regeneration values are halved when applied.
public sealed record ItemSpecTemplate
{
    public static readonly ItemSpecTemplate None = new();

    public ItemFloatRange Absorb { get; init; }
    public ItemRange Defence { get; init; }
    public ItemFloatRange MoveSpeed { get; init; }
    public ItemFloatRange MagicMastery { get; init; }
    public ItemFloatRange ManaRegen { get; init; }
    public ItemRange LevelAttackRating { get; init; }
    public float Block { get; init; }
    public int AttackSpeed { get; init; }
    public int Critical { get; init; }
    public int ShootingRange { get; init; }
    public int LevelMana { get; init; }
    public int LevelLife { get; init; }
    public int LevelDamage { get; init; }
    public float LifeRegen { get; init; }

    // An authored range for the life regeneration; when set it replaces the fixed LifeRegen of a specialized copy.
    public ItemFloatRange LifeRegenRange { get; init; }
    public float StaminaRegen { get; init; }
}
