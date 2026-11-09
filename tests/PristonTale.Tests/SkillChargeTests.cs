using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillChargeTests
{
    private const uint ChargingStrike = 0x03080002;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a held charge released early jumps ahead and strikes partially; held through its window it strikes fully", ChargeAsync);
    }

    private static (SkillScenario Scenario, Simulation.World.Monsters.MonsterInstance Target) Begin()
    {
        var scenario = SkillScenario.Create(PlayerClass.Pikeman, 80, SkillScenario.Weapon(ItemKind.Pole, "WP101")).Learn(ChargingStrike, 5);
        var target = scenario.Spawn(SkillScenario.Enemy(), new Vector3(50, 0, 0));
        Assert.True(scenario.Cast(ChargingStrike, target.EntityId).Accepted, "Charging Strike refused");
        return (scenario, target);
    }

    private static Task ChargeAsync()
    {
        SkillRuleCatalog.TryGet(ChargingStrike, out var rule);
        var timing = SkillFormulas.ChargeTiming(rule, 5);
        Assert.True(timing.StartMs > 0 && timing.FullMs > timing.StartMs && timing.FullHitsAtMs.Length == 3
            && timing.FullHitsAtMs.All(hit => hit >= timing.FullMs && hit <= timing.FullDurationMs + 1), "charge timeline is malformed");

        var (early, earlyTarget) = Begin();
        var releaseTick = (int)MathF.Ceiling((timing.StartMs + 100f) / 50f);
        early.Run(releaseTick);
        Assert.True(early.Player.IsCasting, "the charge ended before its window");
        Assert.True(early.Cast(ChargingStrike, command: SkillCastCommand.Release).Accepted, "the release was refused");
        Assert.True(early.Cast(ChargingStrike, command: SkillCastCommand.Release).Verdict == SkillCastVerdict.Rejected, "a second release was accepted");
        var finished = early.Run(200, () => !early.Player.IsCasting);
        Assert.True(finished > 0 && (releaseTick + finished) * 50f < timing.FullDurationMs - 500f, "an early release did not shorten the motion");
        Assert.True(early.Skills.Any(value => value.Phase == SkillEventPhase.Release && value.SkillCode == ChargingStrike), "the release was not shown");
        var partial = early.Skills.Where(value => value.Phase == SkillEventPhase.Impact && value.SkillCode == ChargingStrike).ToArray();
        Assert.True(partial.Length == 3 && partial.All(value => (value.Flags & SkillEventFlags.Charged) == 0), "a partial charge struck as a full one");
        var struck = early.Combat.Where(value => value.TargetId == earlyTarget.EntityId).Select(value => value.Kind).ToArray();
        Assert.True(struck.Length > 0 && struck.All(kind => kind == CombatEventKind.Hit), $"the partial strikes were not resolved: {string.Join(",", struck)}");

        var (held, _) = Begin();
        var total = held.Run(400, () => !held.Player.IsCasting);
        Assert.True(total * 50f >= timing.FullDurationMs - 100f, "a held charge ended early");
        var full = held.Skills.Where(value => value.Phase == SkillEventPhase.Impact && value.SkillCode == ChargingStrike).ToArray();
        Assert.True(full.Length == 3 && full.All(value => (value.Flags & SkillEventFlags.Charged) != 0), "a held charge did not strike fully");
        Assert.True(!held.Skills.Any(value => value.Phase == SkillEventPhase.Release), "a held charge reported a release");

        var (late, _) = Begin();
        late.Run((int)MathF.Ceiling((timing.FullMs + 60f) / 50f));
        Assert.True(late.Cast(ChargingStrike, command: SkillCastCommand.Release).Verdict == SkillCastVerdict.Rejected, "a release after the full frame shifted the motion");
        return Task.CompletedTask;
    }
}
