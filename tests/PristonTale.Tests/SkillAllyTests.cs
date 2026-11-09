using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Support skills on allies: area buffs on the players (or the party members) around the caster, single-ally buffs at
// reduced strength, group heals and the pulses a field keeps giving to the players inside it.
internal static class SkillAllyTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("an area buff reaches the caster and the allies in range, not the far ones, a party buff only its members", AreaBuffAsync);
        yield return ("a buff cast on another player applies its reduced share and refuses a second copy; an enchant needs a party member", AllyBuffAsync);
        yield return ("a group heal needs a party and heals its members in range, not the caster, and virtual life lowers heals", GroupHealAsync);
        yield return ("a regeneration field pulses short buffs to the allies around its holder", PulseAsync);
    }

    private static Task AreaBuffAsync()
    {
        var field = SkillFixtures.Field();
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        var near = SkillFixtures.Player(field, 2, PlayerClass.Priestess, 40, new Vector3(150, 0, 0));
        var far = SkillFixtures.Player(field, 3, PlayerClass.Magician, 40, new Vector3(400, 0, 0));
        var stranger = SkillFixtures.Player(field, 5, PlayerClass.Fighter, 40, new Vector3(-150, 0, 0));
        SkillFixtures.Group(knight, near, far);
        knight.Character.Skills.Restore(0x05020001, 10);
        SkillFixtures.Accept("Holy Valor", field, knight, 0x05020001);
        SkillFixtures.Finish(field, knight);
        Assert.True(knight.Character.Effects.Totals.Flat(StatKind.DamageVsUndead) == 54, "Holy Valor missing on the caster");
        Assert.True(near.Character.Effects.Totals.Flat(StatKind.DamageVsUndead) == 54, "Holy Valor missing on the near member");
        Assert.True(far.Character.Effects.Totals.Flat(StatKind.DamageVsUndead) == 0, "Holy Valor reached a far member");
        Assert.True(stranger.Character.Effects.Totals.Flat(StatKind.DamageVsUndead) == 0, "Holy Valor reached a player outside the party");

        var atalanta = SkillFixtures.Player(field, 4, PlayerClass.Atalanta, 80, new Vector3(0, 0, 50), SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        atalanta.Character.Skills.Restore(0x06040002, 10);
        atalanta.Character.Skills.Restore(0x06080001, 10);
        SkillFixtures.Accept("Hall of Valhalla", field, atalanta, 0x06080001);
        SkillFixtures.Finish(field, atalanta);
        // Caster: evasion 32 and maximum damage of Triumph rank 10 (50) plus level / 4 (20); allies within 350 get half.
        Assert.True(atalanta.Character.Effects.Totals.Flat(StatKind.Evasion) == 32 && atalanta.Character.Effects.Totals.Flat(StatKind.DamageMax) == 70,
            "Hall of Valhalla values on the caster");
        Assert.True(near.Character.Effects.Totals.Flat(StatKind.Evasion) == 16 && near.Character.Effects.Totals.Flat(StatKind.DamageMax) == 35,
            "Hall of Valhalla share on an ally");
        Assert.True(stranger.Character.Effects.Totals.Flat(StatKind.Evasion) == 16, "Hall of Valhalla reaches any player around, as in the original");
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "Triumph stacked on Hall of Valhalla", field, atalanta, 0x06040002);
        return Task.CompletedTask;
    }

    private static Task AllyBuffAsync()
    {
        var field = SkillFixtures.Field();
        var atalanta = SkillFixtures.Player(field, 1, PlayerClass.Atalanta, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        var ally = SkillFixtures.Player(field, 2, PlayerClass.Knight, 40, new Vector3(120, 0, 0));
        atalanta.Character.Skills.Restore(0x06040002, 10);
        SkillFixtures.Accept("Triumph on an ally", field, atalanta, 0x06040002, ally.EntityId);
        SkillFixtures.Finish(field, atalanta);
        // (50 + 60 / 4) / 2 for another player.
        Assert.True(ally.Character.Effects.Totals.Flat(StatKind.DamageMax) == 32, $"ally share {ally.Character.Effects.Totals.Flat(StatKind.DamageMax)}");
        Assert.True(atalanta.Character.Effects.Totals.Flat(StatKind.DamageMax) == 0, "the caster received an ally's buff");

        var priest = SkillFixtures.Player(field, 3, PlayerClass.Priestess, 60, new Vector3(0, 0, 60), SkillFixtures.Weapon(ItemKind.Staff));
        priest.Character.Skills.Restore(0x07040004, 10);
        var life = ally.Character.MaxLife;
        SkillFixtures.Accept("Virtual Life", field, priest, 0x07040004, ally.EntityId);
        SkillFixtures.Finish(field, priest);
        Assert.True(ally.Character.MaxLife == life * 130 / 100 && ally.Character.Effects.Totals.Percent(StatKind.DamageTaken) == -30, "Virtual Life values");
        SkillFixtures.Wait(field, 20f);
        SkillFixtures.Refuse(SkillCastVerdict.AlreadyActive, "Virtual Life stacked", field, priest, 0x07040004, ally.EntityId);

        var mage = SkillFixtures.Player(field, 4, PlayerClass.Magician, 60, new Vector3(0, 0, -60), SkillFixtures.Weapon(ItemKind.Staff));
        mage.Character.Skills.Restore(0x08020003, 5);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "Enchant Weapon on a player outside the party", field, mage, 0x08020003, ally.EntityId);
        SkillFixtures.Group(mage, ally);
        SkillFixtures.Accept("Enchant Weapon on an ally", field, mage, 0x08020003, ally.EntityId);
        SkillFixtures.Finish(field, mage);
        Assert.True(ally.Character.Effects.TryGet(0x08020003, out _), "enchant missing on the ally");
        return Task.CompletedTask;
    }

    private static Task GroupHealAsync()
    {
        var field = SkillFixtures.Field();
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var hurt = SkillFixtures.Player(field, 2, PlayerClass.Knight, 40, new Vector3(250, 0, 0));
        var away = SkillFixtures.Player(field, 3, PlayerClass.Knight, 40, new Vector3(600, 0, 0));
        var stranger = SkillFixtures.Player(field, 4, PlayerClass.Knight, 40, new Vector3(0, 0, 100));
        priest.Character.Skills.Restore(0x07020004, 1);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "Grand Healing without a party", field, priest, 0x07020004);
        SkillFixtures.Group(priest, hurt, away);
        stranger.Character.Life = 10;
        priest.Character.Life = priest.Character.MaxLife / 2;
        hurt.Character.Life = 10;
        away.Character.Life = 10;
        SkillFixtures.Accept("Grand Healing", field, priest, 0x07020004);
        var heals = SkillFixtures.FinishCollect(field, priest).Where(value => value.Kind == CombatEventKind.Heal).ToList();
        Assert.True(heals.Count == 1 && heals[0].TargetId == hurt.EntityId && heals[0].Damage >= 40, "Grand Healing healed outside its rule");

        hurt.Character.Effects.Apply(new EffectSpec(0x07040004, EffectSourceKind.PlayerSkill, false, [StatModifier.Percent(StatKind.HealingTaken, -50)]), 9, 60000f);
        hurt.Character.Life = 10;
        SkillFixtures.Wait(field, 20f);
        SkillFixtures.Accept("second Grand Healing", field, priest, 0x07020004);
        var lowered = SkillFixtures.FinishCollect(field, priest).Single(value => value.Kind == CombatEventKind.Heal);
        var weapon = SkillFormulas.WeaponItemDamage(priest.Character);
        var maximum = 50 + weapon.Max / 3 + priest.Character.Attributes.Spirit / 6;
        Assert.True(lowered.Damage <= maximum / 2 + 1, $"healing taken did not lower the heal ({lowered.Damage} of at most {maximum})");
        return Task.CompletedTask;
    }

    private static Task PulseAsync()
    {
        var field = SkillFixtures.Field();
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 70, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var ally = SkillFixtures.Player(field, 2, PlayerClass.Fighter, 40, new Vector3(200, 0, 0));
        priest.Character.Skills.Restore(0x07080002, 10);
        SkillFixtures.Accept("Regeneration Field", field, priest, 0x07080002);
        SkillFixtures.Finish(field, priest);
        Assert.True(priest.Character.Effects.Totals.Flat(StatKind.LifeRegen) == 120 && priest.Character.Effects.Totals.Flat(StatKind.ManaRegen) == 100,
            "field values on the holder");
        Assert.True(ally.Character.Effects.TryGet(0x07080002, out var pulse) && pulse.DurationMs == 5000f, "ally received no pulse");
        Assert.True(ally.Character.Effects.Totals.Flat(StatKind.LifeRegen) == 120 && ally.Character.Effects.Totals.Flat(StatKind.ManaRegen) == 50,
            "ally pulse values");
        ally.Teleport(new Vector3(1000, 0, 0));
        SkillFixtures.Wait(field, 6f);
        Assert.True(!ally.Character.Effects.TryGet(0x07080002, out _), "pulse renewed outside the field");
        return Task.CompletedTask;
    }
}
