using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SkillCompanionTests
{
    private const uint Falcon = 0x04020002;
    private const uint MagneticSphere = 0x01080003;

    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a falcon strikes only its owner's target on its interval, and stops with its buff", FalconAsync);
        yield return ("magnetic orbs strike the nearest enemy within their radius once per interval", OrbsAsync);
    }

    private static int Strikes(SkillScenario scenario, uint code) =>
        scenario.Skills.Count(value => value.SkillCode == code && (value.Flags & SkillEventFlags.Companion) != 0);

    private static Task FalconAsync()
    {
        var archer = SkillScenario.Create(PlayerClass.Archer, 60).Learn(Falcon, 1);
        var prey = archer.Spawn(SkillScenario.Enemy(), new Vector3(150, 0, 0));
        Assert.True(archer.Cast(Falcon).Accepted, "Falcon refused");
        archer.Run(60, () => archer.Character.Effects.TryGet(Falcon, out _));
        archer.Run(20 * 6);
        Assert.True(Strikes(archer, Falcon) == 0 && prey.Life == prey.Def.Combat.MaxLife, "the falcon struck while its owner was not fighting");

        Assert.True(archer.Field.SetAttackTarget(archer.Player.EntityId, prey.EntityId, true), "attack refused");
        archer.Player.CombatTargetId = prey.EntityId;
        archer.Run(20 * 10);
        var strikes = Strikes(archer, Falcon);
        Assert.True(strikes is >= 3 and <= 5, $"{strikes} falcon strikes in ten seconds at a 2.2 s interval");
        var struck = archer.Skills.Where(value => value.SkillCode == Falcon && (value.Flags & SkillEventFlags.Companion) != 0);
        Assert.True(struck.All(value => value.TargetId == prey.EntityId), "the falcon struck another enemy than its owner's target");

        var changes = new List<Simulation.Modifiers.EffectChange>();
        archer.Character.Effects.Remove(Falcon, archer.Player.EntityId, changes);
        var before = Strikes(archer, Falcon);
        archer.Run(20 * 6);
        Assert.True(Strikes(archer, Falcon) == before, "the falcon kept striking after its buff ended");
        return Task.CompletedTask;
    }

    private static Task OrbsAsync()
    {
        var mechanic = SkillScenario.Create(PlayerClass.Mechanician, 80).Learn(MagneticSphere, 1);
        var near = mechanic.Spawn(SkillScenario.Enemy(), new Vector3(200, 0, 0));
        var far = mechanic.Spawn(SkillScenario.Enemy(), new Vector3(0, 0, 420));
        Assert.True(mechanic.Cast(MagneticSphere).Accepted, "Magnetic Sphere refused");
        mechanic.Run(60, () => mechanic.Character.Effects.TryGet(MagneticSphere, out _));
        mechanic.Run(20 * 13);
        var strikes = mechanic.Skills.Where(value => value.SkillCode == MagneticSphere && (value.Flags & SkillEventFlags.Companion) != 0).ToArray();
        Assert.True(strikes.Length == 2 && strikes.All(value => value.TargetId == near.EntityId), $"{strikes.Length} orb strikes on the near enemy in 13 s");
        var dealt = near.Def.Combat.MaxLife - near.Life;
        Assert.True(dealt is >= 2 and <= 2 * 40 && far.Life == far.Def.Combat.MaxLife, $"orb damage {dealt} outside rank 1 or the far enemy was hit");
        return Task.CompletedTask;
    }
}
