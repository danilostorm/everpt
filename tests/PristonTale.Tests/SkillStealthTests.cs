using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillStealthTests
{
    private const uint Vanish = 0x03040002;
    private const uint VenomSpear = 0x03040001;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("Vanish shortens monster sight, empowers the next hit and ends on that hit or on an area attack", VanishAsync);
    }

    private static SkillScenario Pikeman() =>
        SkillScenario.Create(PlayerClass.Pikeman, 100, SkillScenario.Weapon(ItemKind.Pole, "WP101")).Learn(Vanish, 10).Learn(VenomSpear, 1);

    private static Task VanishAsync()
    {
        var seen = Pikeman();
        var watcher = seen.Spawn(SkillScenario.Enemy(sight: 200), new Vector3(150, 0, 0));
        Assert.True(seen.Run(20 * 4, () => watcher.TargetId == seen.Player.EntityId) > 0, "control: a monster in sight never noticed the player");

        var hidden = Pikeman();
        Assert.True(hidden.Cast(Vanish).Accepted, "Vanish refused");
        hidden.Run(40, () => hidden.Character.Effects.TryGet(Vanish, out _));
        Assert.True(hidden.Character.Effects.TryGet(Vanish, out var vanish) && vanish.RemainingMs > 260000f, "Vanish did not start with its rank 10 duration");
        var totals = hidden.Character.Effects.Totals;
        Assert.True(totals.Flat(StatKind.MonsterSight) == -90 && totals.Flat(StatKind.NextHitDamage) == 100, "Vanish modifiers differ from rank 10");
        var blind = hidden.Spawn(SkillScenario.Enemy(sight: 200), new Vector3(150, 0, 0));
        hidden.Run(20 * 4);
        Assert.True(blind.TargetId != hidden.Player.EntityId, "a monster saw a vanished player at 150 units with 20 units of sight");

        blind.Position = new Vector3(40, 0, 0);
        var life = blind.Life;
        Assert.True(hidden.Field.SetAttackTarget(hidden.Player.EntityId, blind.EntityId, true), "attack refused");
        Assert.True(hidden.Run(20 * 3, () => blind.Life < life) > 0, "the attack never landed");
        Assert.True(!hidden.Character.Effects.TryGet(Vanish, out _), "Vanish survived the hit it empowered");
        Assert.True(hidden.Character.Effects.Totals.Flat(StatKind.NextHitDamage) == 0, "the next-hit bonus stayed after the hit");

        var area = Pikeman();
        area.Spawn(SkillScenario.Enemy(), new Vector3(60, 0, 0));
        area.Cast(Vanish);
        area.Run(40, () => area.Character.Effects.TryGet(Vanish, out _));
        area.Run(100, () => !area.Player.IsBusy);
        area.Refill();
        var spear = area.Cast(VenomSpear);
        Assert.True(spear.Accepted, $"Venom Spear refused: {spear.Verdict}");
        area.Run(60);
        Assert.True(!area.Character.Effects.TryGet(Vanish, out _), "Vanish survived an area attack");
        return Task.CompletedTask;
    }
}
