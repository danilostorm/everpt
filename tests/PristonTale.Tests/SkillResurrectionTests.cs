using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Revive skills: a dead party member near the caster rises where it fell with half its life and a short guard.
internal static class SkillResurrectionTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("resurrection revives the nearest dead party member with half life and a guard, and needs one", ReviveAsync);
    }

    private static Task ReviveAsync()
    {
        var field = SkillFixtures.Field();
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        var fallen = SkillFixtures.Player(field, 2, PlayerClass.Knight, 40, new Vector3(200, 0, 0));
        priest.Character.Skills.Restore(0x07040002, 10);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "resurrection cast without a dead ally", field, priest, 0x07040002);
        fallen.ApplyDamage(fallen.Life + 1000);
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "resurrection of a player outside the party", field, priest, 0x07040002);
        fallen.Revive(fallen.MaxLife, 0f);
        SkillFixtures.Group(priest, fallen);
        var revived = false;
        var fellAt = Vector3.Zero;
        for (var attempt = 0; attempt < 6 && !revived; attempt++)
        {
            fallen.ApplyDamage(fallen.Life + 1000);
            fellAt = fallen.Position;
            Assert.True(fallen.IsDead, "fixture did not kill the ally");
            SkillFixtures.Accept("resurrection", field, priest, 0x07040002);
            var events = SkillFixtures.FinishCollect(field, priest);
            revived = !fallen.IsDead && events.Any(value => value.Kind == CombatEventKind.Revive && value.TargetId == fallen.EntityId);
            if (!revived)
            {
                SkillFixtures.Wait(field, 18f);
            }
        }

        Assert.True(revived, "a 94% resurrection never succeeded");
        Assert.True(fallen.Position == fellAt && Math.Abs(fallen.Life - fallen.MaxLife / 2) <= fallen.MaxLife / 50 + 5, $"revived with {fallen.Life}/{fallen.MaxLife}");
        var life = fallen.Life;
        fallen.ApplyDamage(50);
        Assert.True(fallen.Life == life, "the revive guard let damage through");
        SkillFixtures.Wait(field, 6f);
        var guarded = fallen.Life;
        fallen.ApplyDamage(50);
        Assert.True(fallen.Life == guarded - 50, "damage after the guard was ignored");
        return Task.CompletedTask;
    }
}
