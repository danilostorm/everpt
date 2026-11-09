using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal static class SummonAllianceTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a game master's pet and crystal pets fight the wild monsters, never one another, and outlast weak ones", AlliesAsync);
    }

    private static Task AlliesAsync()
    {
        var weak = SkillScenario.Enemy("Weak", life: 300, sight: 2000, level: 5, damage: 8, defence: 10);
        var strong = SkillScenario.SummonModel("Strong", 90) with
        {
            Combat = new MonsterCombatStats(100, 3000, new MonsterRange(400, 500), 8, 120, 2000, 30, 0, 60000, ResistanceBlock.Zero)
        };
        var hopy = SkillScenario.SummonModel("HOPY", 50) with
        {
            Key = "HOPY 2",
            Combat = new MonsterCombatStats(4, 400, new MonsterRange(5, 6), 6, 50, 50, 5, 0, 150, ResistanceBlock.Zero)
        };
        var scenario = SkillScenario.Create(PlayerClass.Fighter, 100, null, null, weak, strong, hopy);
        scenario.Character.Life = scenario.Character.MaxLife;
        var pet = scenario.Field.CallGameMasterPet(scenario.Player, strong, 240_000f);
        Assert.True(scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2") is not null
            && scenario.Field.CallCrystalMonster(scenario.Player, "GP101", "HOPY 2") is not null, "the crystal pets were not called");
        var wild = Enumerable.Range(0, 6).Select(index => scenario.Spawn(weak, new Vector3(200f + index * 30f, 0f, 120f))).ToList();
        var summons = scenario.Summons.Select(static summon => summon.EntityId).ToHashSet();
        Assert.True(summons.Count == 3, "the three pets are not all owned");

        for (var second = 0; second < 60; second++)
        {
            scenario.Character.Life = scenario.Character.MaxLife;
            scenario.Run(20);
        }

        Assert.True(!scenario.Combat.Any(value => summons.Contains(value.AttackerId) && summons.Contains(value.TargetId)),
            "one pet swung at or hit another");
        Assert.True(wild.All(static monster => !monster.IsAlive), "the pets did not hunt the wild monsters");
        Assert.True(pet.IsAlive && pet.Life > pet.Def.Combat.MaxLife * 9 / 10, $"weak monsters wore the strong pet down to {pet.Life}");
        return Task.CompletedTask;
    }
}
