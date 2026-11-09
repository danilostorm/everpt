using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class PassiveSkillTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("passive masteries use weapon damage and resistances preserve ten original ranks", PassivesAsync);
        yield return ("physical training recomputes once and rejects foreign skills and attribute overflow", RestoreAsync);
    }

    private static Task PassivesAsync()
    {
        var cases = new[]
        {
            (PlayerClass.Fighter, 0x02010001u, ItemKind.Axe, new[] { 6, 10, 14, 18, 21, 24, 26, 28, 30, 32 }),
            (PlayerClass.Archer, 0x04010002u, ItemKind.Bow, new[] { 4, 8, 12, 16, 20, 24, 28, 32, 36, 40 }),
            (PlayerClass.Atalanta, 0x06010003u, ItemKind.ThrowingWeapon, new[] { 18, 22, 26, 30, 34, 38, 41, 44, 47, 50 })
        };
        foreach (var (job, code, kind, percentages) in cases)
        {
            for (var rank = 1; rank <= 10; rank++)
            {
                var player = new CharacterClassRegistry().CreatePlayer(job, "Mastery", 40);
                var weapon = Weapon(kind);
                player.Equip(EquipmentSlot.Weapon, weapon);
                var before = WeaponDamage.Compute(player);
                player.Skills.Restore(code, rank);
                var after = CombatProfileFactory.ForPlayerBasicAttack(player);
                Assert.True(after.DamageMin - before.Min == (int)(percentages[rank - 1] / 100f * 37)
                    && after.DamageMax - before.Max == (int)(percentages[rank - 1] / 100f * 73), "mastery scaled the final damage or rounded incorrectly");
                player.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Staff));
                var unskilled = new CharacterClassRegistry().CreatePlayer(job, "Control", 40);
                unskilled.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Staff));
                Assert.True(WeaponDamage.Compute(player) == WeaponDamage.Compute(unskilled), "mastery applied to an unsupported weapon");
                player.Equip(EquipmentSlot.Weapon, null!);
                unskilled.Equip(EquipmentSlot.Weapon, null!);
                Assert.True(WeaponDamage.Compute(player) == WeaponDamage.Compute(unskilled), "mastery applied to bare hands");
            }
        }

        var resistanceCases = new[]
        {
            (PlayerClass.Mechanician, 0x01010004u, Element.Poison, new[] { 9, 17, 24, 30, 35, 39, 42, 44, 45, 46 }),
            (PlayerClass.Fighter, 0x02010002u, Element.Fire, new[] { 9, 17, 24, 30, 35, 39, 42, 44, 45, 46 }),
            (PlayerClass.Pikeman, 0x03010002u, Element.Ice, new[] { 8, 15, 21, 26, 30, 33, 36, 39, 42, 45 })
        };
        foreach (var (job, code, element, values) in resistanceCases)
        {
            for (var rank = 1; rank <= 10; rank++)
            {
                var player = new CharacterClassRegistry().CreatePlayer(job, "Resistance", 40);
                player.Skills.Restore(code, rank);
                var resistance = CombatProfileFactory.ForPlayerDefender(player).Resistances;
                foreach (var tested in Enum.GetValues<Element>())
                {
                    Assert.True(resistance[tested] == (tested == element ? values[rank - 1] : 0), "passive changed the wrong resistance");
                }
            }
        }

        return Task.CompletedTask;
    }

    private static Task RestoreAsync()
    {
        var percentages = new[] { 5, 8, 11, 14, 17, 20, 23, 26, 28, 30 };
        for (var rank = 1; rank <= 10; rank++)
        {
            var player = new CharacterClassRegistry().CreatePlayer(PlayerClass.Knight, "Training", 40);
            var maximum = player.MaxStamina;
            player.Stamina = 17;
            player.Skills.Restore(0x05010003, rank);
            var expected = maximum + (int)(percentages[rank - 1] * (float)maximum / 100f);
            Assert.True(player.MaxStamina == expected && player.Stamina == 17, "training restored current SP or changed formula");
            var clone = player.CloneForInventoryOperation();
            Assert.True(clone.MaxStamina == expected && clone.Stamina == 17, "transaction clone lost or doubled training");
            player.Equip(EquipmentSlot.Weapon, Weapon(ItemKind.Sword));
            Assert.True(player.MaxStamina == expected, "recompute compounded training");
            Assert.Throws<ArgumentException>(() => player.Skills.Restore(0x02010001, 1), "a foreign class skill was restored");
            Assert.True(player.Skills.Ranks.Count == 1, "foreign class skill changed ownership before rejection");
        }

        var overflow = new CharacterClassRegistry().CreatePlayer(PlayerClass.Fighter, "Overflow");
        overflow.RestoreProgress(0, 0, int.MaxValue);
        var attributes = overflow.Attributes;
        Assert.True(!overflow.TryAllocateAttribute(CharacterAttribute.Strength, int.MaxValue)
            && overflow.Attributes == attributes && overflow.StatPoints == int.MaxValue, "overflow spent points or corrupted attributes");
        return Task.CompletedTask;
    }

    private static ItemInstance Weapon(ItemKind kind) => new(new ItemDefinition(1, "Fixture", kind, EquipmentSlot.Weapon)
    {
        BaseStats = new ItemStatBlock(37, 73, 0, 0, 0, 0, 0, 0, 0, 0)
    });
}
