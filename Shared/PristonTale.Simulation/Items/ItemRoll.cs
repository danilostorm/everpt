using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;

namespace PristonTale.Simulation.Items;

// The values of one item copy. Absorb keeps one decimal, block two; requirements and price already include the
// class specialization adjustments.
public sealed record ItemRoll(
    int DamageMin,
    int DamageMax,
    int AttackRating,
    int Defence,
    float Absorb,
    float Block,
    float MoveSpeed,
    float LifeRegen,
    float ManaRegen,
    float StaminaRegen,
    int AddLife,
    int AddMana,
    int AddStamina,
    ResistanceBlock Resistances,
    int RequiredLevel,
    CharacterAttributes Requirements,
    int Price,
    uint SpecJob,
    ItemSpecBonus Spec)
{
    public int CriticalBonus { get; init; }
    public int AttackSpeedBonus { get; init; }

    // The magic mastery the copy rolled above its definition's, the low end of the authored range.
    public float MagicMasteryBonus { get; init; }

    public bool SpecializedFor(JobBitCode job) => (SpecJob & (uint)job) != 0;
}

// The class specialization bonuses of one copy. LevelLife, LevelMana, LevelAttackRating and LevelDamage are level
// divisors (zero grants nothing).
public sealed record ItemSpecBonus(
    float Absorb,
    int Defence,
    float MoveSpeed,
    float Block,
    int AttackSpeed,
    int Critical,
    int ShootingRange,
    float MagicMastery,
    int LevelLife,
    int LevelMana,
    int LevelAttackRating,
    int LevelDamage,
    float LifeRegen,
    float ManaRegen,
    float StaminaRegen)
{
    public static readonly ItemSpecBonus None = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}
