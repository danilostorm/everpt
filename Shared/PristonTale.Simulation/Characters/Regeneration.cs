using PristonTale.Simulation.Modifiers;

namespace PristonTale.Simulation.Characters;

public enum RegenerationPosture
{
    Standing,
    Walking,
    Running,
    // Attacking, casting, flinching or eating: stamina neither recovers nor drains.
    Acting
}

// Natural recovery of life, mana and stamina per second, as the original character loop computes it, plus the regen
// modifiers (tenths of a point per second) of items, skills and other effects. The original applies value / 17 every
// four 70 Hz ticks, about 3% more than the per-second value.
public static class Regeneration
{
    private const float AppliedRate = 17.5f / 17f;

    public static float LifePerSecond(PlayerCharacter character)
    {
        var attributes = character.Attributes;
        var natural = (character.Level + attributes.Strength / 2 + attributes.Health) / 180f;
        var bonus = character.Effects.Totals.Flat(StatKind.LifeRegen) / 10f + character.Derived.LifeRegen;
        return (natural + bonus) / 1.5f * AppliedRate;
    }

    public static float ManaPerSecond(PlayerCharacter character)
    {
        var attributes = character.Attributes;
        var natural = (character.Level + attributes.Spirit * 1.2f + attributes.Health / 2) / 115f;
        var bonus = character.Effects.Totals.Flat(StatKind.ManaRegen) / 10f + character.Derived.ManaRegen;
        return (natural + bonus) * AppliedRate;
    }

    // Positive while standing or walking; negative (the running drain) while running.
    public static float StaminaPerSecond(PlayerCharacter character, RegenerationPosture posture)
    {
        switch (posture)
        {
            case RegenerationPosture.Running:
                // Integer division of the carried weight against the limit: 1.4 per second while light, more when heavily loaded.
                var drain = (1000 + character.CarriedWeight) / Math.Max(1, character.Derived.MaxWeight + character.Attributes.Strength / 2 + 500) + 0.4f;
                return -drain;
            case RegenerationPosture.Acting:
                return 0f;
        }

        var natural = character.Definition.FunctionProfile.StaminaFunction switch
        {
            1 => 3.8f,
            2 => 3.3f,
            _ => 2.9f
        };
        var perSecond = (natural + character.Level / 7 + character.Effects.Totals.Flat(StatKind.StaminaRegen) / 10f + character.Derived.StaminaRegen) * AppliedRate;
        return posture == RegenerationPosture.Walking ? perSecond * 0.6f : perSecond;
    }
}

// Fractional accumulators of one character's recovery; the server ticks it and applies whole points.
public sealed class RegenerationState
{
    private float _life;
    private float _mana;
    private float _stamina;

    // Returns true when a pool changed.
    public bool Step(PlayerCharacter character, RegenerationPosture posture, float dtMs)
    {
        if (dtMs <= 0f || !float.IsFinite(dtMs) || character.Life <= 0)
        {
            return false;
        }

        var seconds = dtMs / 1000f;
        var changed = false;
        _life += Regeneration.LifePerSecond(character) * seconds;
        _mana += Regeneration.ManaPerSecond(character) * seconds;
        _stamina += Regeneration.StaminaPerSecond(character, posture) * seconds;
        if (Apply(ref _life, character.Life, character.MaxLife, out var life))
        {
            character.Life = life;
            changed = true;
        }

        if (Apply(ref _mana, character.Mana, character.MaxMana, out var mana))
        {
            character.Mana = mana;
            changed = true;
        }

        if (Apply(ref _stamina, character.Stamina, character.MaxStamina, out var stamina))
        {
            character.Stamina = stamina;
            changed = true;
        }

        return changed;
    }

    public void Reset()
    {
        _life = 0f;
        _mana = 0f;
        _stamina = 0f;
    }

    private static bool Apply(ref float accumulator, int current, int maximum, out int next)
    {
        next = current;
        var whole = (int)accumulator;
        if (whole == 0)
        {
            return false;
        }

        accumulator -= whole;
        next = Math.Clamp(current + whole, 0, maximum);
        if (next == current)
        {
            // A full pool does not bank recovery for later.
            accumulator = 0f;
            return false;
        }

        return true;
    }
}
