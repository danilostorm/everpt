using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Multi-target skills: chains that jump between enemies, capped areas, life-share drains and undead-only life cuts.
internal static class SkillAreaTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("chain lightning links from the target to the nearest enemies within its link length", ChainAsync);
        yield return ("divine lightning strikes at most its rank's number of enemies around the caster", CappedAsync);
        yield return ("soul sucker drains a share of maximum life and returns it to the caster", DrainAsync);
        yield return ("extinction cuts a share of an undead monster's life and spares the others", ExtinctionAsync);
    }

    private static Task ChainAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var field = SkillFixtures.Field(7, definition);
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 80, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        priest.Character.Skills.Restore(0x07080003, 1);
        // Rank 1: five links of at most 140 units; the last monster stands 200 beyond the fourth link.
        var monsters = new[] { 100f, 220f, 340f, 460f, 660f }.Select(x => SkillFixtures.Spawn(field, definition, new Vector3(x, 0, 0))).ToArray();
        var hits = new List<CombatEventView>();
        var links = 0;
        SkillFixtures.Accept("Chain Lightning", field, priest, 0x07080003, monsters[0].EntityId);
        for (var tick = 0; tick < 100; tick++)
        {
            field.Step(0.05f);
            hits.AddRange((field.SnapshotEvents() ?? []).Where(value => value.Kind == CombatEventKind.Hit));
            links += field.SnapshotSkillEvents().Count(value => (value.Flags & SkillEventFlags.Chain) != 0);
        }

        var struck = hits.Select(value => value.TargetId).ToHashSet();
        Assert.True(struck.SetEquals(monsters.Take(4).Select(monster => monster.EntityId)), $"chain struck {string.Join(",", struck)}");
        Assert.True(links == 3, $"chain reported {links} links");
        return Task.CompletedTask;
    }

    private static Task CappedAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var field = SkillFixtures.Field(9, definition);
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        priest.Character.Skills.Restore(0x07020002, 1);
        for (var index = 0; index < 8; index++)
        {
            SkillFixtures.Spawn(field, definition, new Vector3(MathF.Sin(index) * 120, 0, MathF.Cos(index) * 120));
        }

        SkillFixtures.Accept("Divine Lightning", field, priest, 0x07020002);
        var hits = SkillFixtures.FinishCollect(field, priest).Count(value => value.Kind == CombatEventKind.Hit);
        Assert.True(hits == 3, $"rank 1 struck {hits} enemies instead of 3");
        return Task.CompletedTask;
    }

    private static Task DrainAsync()
    {
        var definition = SkillFixtures.Monster(life: 1000);
        var field = SkillFixtures.Field(4, definition);
        var atalanta = SkillFixtures.Player(field, 1, PlayerClass.Atalanta, 40, Vector3.Zero, SkillFixtures.Weapon(ItemKind.ThrowingWeapon),
            SkillFixtures.Shield());
        atalanta.Character.Skills.Restore(0x06020003, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(0, 0, 100));
        var weak = SkillFixtures.Spawn(field, definition, new Vector3(0, 0, 150));
        weak.Life = 400;
        atalanta.Character.Life = 10;
        var request = new Simulation.Skills.SkillCastRequest(1, 0x06020003, monster.EntityId, new Vector3(0, 0, -100), true);
        Assert.True(field.CastSkill(atalanta, request).Accepted, "Soul Sucker refused");
        SkillFixtures.Finish(field, atalanta);
        Assert.True(monster.Life == 1000 - 370, $"Soul Sucker took {1000 - monster.Life} life instead of 37%");
        Assert.True(weak.Life == 400, "Soul Sucker drained a monster below half life");
        Assert.True(atalanta.Character.Life >= 10 + Math.Min(370, atalanta.Character.MaxLife * 40 / 100), "the drained life did not return");
        return Task.CompletedTask;
    }

    private static Task ExtinctionAsync()
    {
        var undead = SkillFixtures.Monster(MonsterBrood.Undead, life: 1000);
        var normal = SkillFixtures.Monster(MonsterBrood.Normal, life: 1000) with { Code = 9 };
        var field = SkillFixtures.Field(13, undead, normal);
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        priest.Character.Skills.Restore(0x07040003, 10);
        var ghost = SkillFixtures.Spawn(field, undead, new Vector3(80, 0, 0));
        var beast = SkillFixtures.Spawn(field, normal, new Vector3(-80, 0, 0));
        SkillFixtures.Accept("Extinction", field, priest, 0x07040003);
        SkillFixtures.Finish(field, priest);
        // Rank 10 at level 60: 96 + 12 percent chance (always), 52 percent of the current life.
        Assert.True(ghost.Life == 1000 - 520 && beast.Life == 1000, $"Extinction left {ghost.Life} / {beast.Life}");
        return Task.CompletedTask;
    }
}
