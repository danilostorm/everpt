using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.Items;

// How item copies are made from their authored ranges. A shop sells the lowest form of an item; a monster drop rolls
// every range, and may specialize the copy for a class.
public static partial class ItemRoller
{
    public static int LowerDurability(ItemDefinition definition)
    {
        var template = definition.Template;
        if (template is null)
        {
            return definition.MaxDurability;
        }

        return template.Durability.Min;
    }

    public static ItemRoll Lower(ItemDefinition definition)
    {
        var template = definition.Template;
        if (template is null)
        {
            var stats = definition.BaseStats;
            return new ItemRoll(stats.DamageMin, stats.DamageMax, stats.AttackRating, stats.Defence, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                definition.Resistances, definition.RequiredLevel, definition.Requirements, definition.Price, definition.SpecializationJobs,
                ItemSpecBonus.None);
        }

        var resistances = template.Resistances;
        var roll = new ItemRoll(
            template.DamageMin.Max != 0 || template.DamageMax.Max != 0 ? template.DamageMin.Min : 0,
            template.DamageMin.Max != 0 || template.DamageMax.Max != 0 ? template.DamageMax.Min : 0,
            template.AttackRating.Min, template.Defence.Min, template.Absorb.Min, template.Block.Min, template.MoveSpeed.Min,
            template.LifeRegen.Min, template.ManaRegen.Min, template.StaminaRegen.Min,
            template.AddLife.Min, template.AddMana.Min, template.AddStamina.Min,
            new ResistanceBlock(Low(resistances, 0), Low(resistances, 1), Low(resistances, 2), Low(resistances, 3), Low(resistances, 4),
                Low(resistances, 5), Low(resistances, 6), Low(resistances, 7)),
            definition.RequiredLevel, definition.Requirements, definition.Price, template.SpecJob, ItemSpecBonus.None);
        if (template.SpecJob == 0)
        {
            return roll;
        }

        // The lowest form keeps its authored specialization with the middle of each requirement shift.
        var spec = template.Spec;
        var requirements = Shift(definition.Requirements, template.SpecJob, static range => range.Min + (range.Max - range.Min) / 2);
        var levelRating = spec.LevelAttackRating.Max != 0
            ? spec.LevelAttackRating.Min + (spec.LevelAttackRating.Max - spec.LevelAttackRating.Min) / 2
            : spec.LevelAttackRating.Min;
        var bonus = new ItemSpecBonus(spec.Absorb.Min, spec.Defence.Min, spec.MoveSpeed.Min, spec.Block, spec.AttackSpeed, spec.Critical,
            spec.ShootingRange, spec.MagicMastery.Min, spec.LevelLife, spec.LevelMana, levelRating, spec.LevelDamage,
            spec.LifeRegenRange.Max != 0 ? spec.LifeRegenRange.Min : spec.LifeRegen, spec.ManaRegen.Min, spec.StaminaRegen);
        return roll with { Requirements = requirements, Spec = bonus };
    }

    // A dropped copy: every range is rolled; four times in ten the copy is specialized, taking one of the random
    // classes three times in ten when the item has any, and then costs a fifth more.
    public static ItemInstance Create(ItemDefinition definition, int count, ISimRng rng) => Create(definition, count, rng, ItemRollOverrides.None);

    private static CharacterAttributes Shift(CharacterAttributes requirements, uint job, Func<ItemRange, int> percent)
    {
        if (JobBits.ClassOf((JobBitCode)job) is not { } owner || ClassProfiles.Of(owner).Shift is not { } shift)
        {
            return requirements;
        }

        return new CharacterAttributes(
            Apply(requirements.Strength, shift.Strength, percent),
            Apply(requirements.Spirit, shift.Spirit, percent),
            Apply(requirements.Talent, shift.Talent, percent),
            Apply(requirements.Agility, shift.Agility, percent),
            requirements.Health);
    }

    private static int Apply(int requirement, ItemRange shift, Func<ItemRange, int> percent)
    {
        if (requirement == 0 || shift.Min == 0)
        {
            return requirement;
        }

        return requirement + requirement * percent(shift) / 100;
    }

    private static short Low(IReadOnlyList<ItemRange> ranges, int element) => (short)(element < ranges.Count ? ranges[element].Min : 0);

    private static int Range(ISimRng rng, ItemRange range) => range.Max <= range.Min ? range.Max : rng.NextInclusive(range.Min, range.Max);

    // One decimal: absorb, move speed and regeneration.
    private static float Tenths(ISimRng rng, ItemFloatRange range) => Steps(rng, range, 10f);

    // Two decimals: block and the specialization ranges.
    private static float Hundredths(ISimRng rng, ItemFloatRange range) => Steps(rng, range, 100f);

    private static float Steps(ISimRng rng, ItemFloatRange range, float steps)
    {
        if (range.Max == 0)
        {
            return range.Min;
        }

        var span = (int)((range.Max - range.Min) * steps);
        if (span <= 0)
        {
            return range.Max;
        }

        return range.Min + rng.NextInclusive(0, span) / steps;
    }
}
