using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Weapon element enchants on the holder's hits and the companions that strike for the holder.
internal static class SkillEnchantTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("an enchanted weapon adds its element damage to basic attacks", EnchantAsync);
        yield return ("a frost javelin slows the monsters its basic attacks strike and ends without a javelin", FrostAsync);
        yield return ("a dancing sword strikes the nearest enemy once per interval while it lasts", CompanionAsync);
    }

    private static Task EnchantAsync()
    {
        var definition = SkillFixtures.Monster(life: 10_000_000);
        var field = SkillFixtures.Field(21, definition);
        var mage = SkillFixtures.Player(field, 1, PlayerClass.Magician, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff, 20, 20));
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(60, 0, 0));
        var plain = AverageSwing(field, mage, monster);
        mage.Character.Skills.Restore(0x08020003, 10);
        SkillFixtures.Accept("Enchant Weapon", field, mage, 0x08020003);
        SkillFixtures.Finish(field, mage);
        Assert.True(mage.Character.Effects.TryGet(0x08020003, out _), "enchant missing on the caster");
        var enchanted = AverageSwing(field, mage, monster);
        // Rank 10 adds 30-44 ice, 22-53 lightning or 40-64 fire.
        Assert.True(enchanted - plain is > 25 and < 70, $"enchant added {enchanted - plain:0.0} per hit");
        return Task.CompletedTask;
    }

    private static Task FrostAsync()
    {
        var definition = SkillFixtures.Monster(life: 10_000_000);
        var field = SkillFixtures.Field(23, definition);
        var atalanta = SkillFixtures.Player(field, 1, PlayerClass.Atalanta, 70, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon));
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(60, 0, 0));
        atalanta.Character.Skills.Restore(0x06080003, 10);
        SkillFixtures.Accept("Frost Javelin", field, atalanta, 0x06080003);
        SkillFixtures.Finish(field, atalanta);
        for (var swing = 0; swing < 30 && !monster.Effects.Has(ControlFlags.Slow); swing++)
        {
            field.Combat.ResolvePlayerSwingHit(atalanta, monster.EntityId, field.NowMs);
        }

        Assert.True(monster.Effects.Has(ControlFlags.Slow) && monster.Effects.Totals.Percent(StatKind.MoveSpeed) == -21, "Frost Javelin did not slow");
        atalanta.Character.Equip(EquipmentSlot.Weapon, SkillFixtures.Weapon(ItemKind.Bow));
        field.Step(0.05f);
        Assert.True(!atalanta.Character.Effects.TryGet(0x06080003, out _), "Frost Javelin outlived the javelin");
        return Task.CompletedTask;
    }

    private static Task CompanionAsync()
    {
        var definition = SkillFixtures.Monster(life: 10_000_000);
        var field = SkillFixtures.Field(25, definition);
        var mage = SkillFixtures.Player(field, 1, PlayerClass.Magician, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(150, 0, 0));
        SkillFixtures.Spawn(field, definition, new Vector3(1000, 0, 0));
        mage.Character.Skills.Restore(0x08040004, 1);
        SkillFixtures.Accept("Dancing Sword", field, mage, 0x08040004);
        var strikes = 0;
        var targets = new HashSet<ulong>();
        for (var tick = 0; tick < 12 * 20; tick++)
        {
            field.Step(0.05f);
            foreach (var hit in (field.SnapshotEvents() ?? []).Where(value => value.Kind == CombatEventKind.Hit && value.AttackerId == mage.EntityId))
            {
                strikes++;
                targets.Add(hit.TargetId);
            }
        }

        // Rank 1 strikes every 5 s: twice in 12 s, only the monster within reach.
        Assert.True(strikes == 2 && targets.SetEquals([monster.EntityId]), $"companion struck {strikes} times at {string.Join(",", targets)}");
        return Task.CompletedTask;
    }

    private static double AverageSwing(FieldInstance field, WorldPlayer player, MonsterInstance monster)
    {
        var total = 0L;
        var landed = 0;
        for (var swing = 0; swing < 400; swing++)
        {
            var life = monster.Life;
            field.Combat.ResolvePlayerSwingHit(player, monster.EntityId, field.NowMs);
            if (monster.Life < life)
            {
                total += life - monster.Life;
                landed++;
            }
        }

        Assert.True(landed > 100, "too few basic attacks landed");
        return total / (double)landed;
    }
}
