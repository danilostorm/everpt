using System.Numerics;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Control effects of the later tiers: charming undead, slowing areas and how a slowed or charmed monster acts.
internal static class SkillControlTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("holy incantation charms an undead monster, raises its life and keeps it out of the fight", CharmAsync);
        yield return ("glacial spike and distortion slow the monsters they reach, ice resistance shortens the chill", SlowAsync);
        yield return ("roar stuns the enemies around the caster until damage breaks the stun", RoarAsync);
        yield return ("venom spear poisons its targets without killing them", PoisonAsync);
        yield return ("compulsion turns the enemies around the caster on it and hardens the caster", TauntAsync);
    }

    private static Task CharmAsync()
    {
        var undead = SkillFixtures.Monster(MonsterBrood.Undead, life: 1000);
        var normal = SkillFixtures.Monster(MonsterBrood.Normal, life: 1000) with { Code = 9 };
        var field = SkillFixtures.Field(31, undead, normal);
        var knight = SkillFixtures.Player(field, 1, PlayerClass.Knight, 60, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Sword));
        knight.Character.Skills.Restore(0x05040003, 10);
        var beast = SkillFixtures.Spawn(field, normal, new Vector3(80, 0, 0));
        SkillFixtures.Refuse(SkillCastVerdict.InvalidTarget, "charm accepted a living beast", field, knight, 0x05040003, beast.EntityId);
        var ghost = SkillFixtures.Spawn(field, undead, new Vector3(0, 0, 80));
        var charmed = false;
        for (var attempt = 0; attempt < 6 && !charmed; attempt++)
        {
            SkillFixtures.Accept("Holy Incantation", field, knight, 0x05040003, ghost.EntityId);
            SkillFixtures.Finish(field, knight);
            charmed = ghost.Effects.Has(ControlFlags.Charmed);
            if (!charmed)
            {
                SkillFixtures.Wait(field, 18f);
            }
        }

        Assert.True(charmed && ghost.Life == 2800, $"charm failed or left life {ghost.Life}");
        Assert.True(!field.SetAttackTarget(knight.EntityId, ghost.EntityId, true), "a charmed monster can still be attacked");
        var prey = new FieldPreyFixture(knight.EntityId, knight.Position);
        new MonsterAiBrain(new CombatRng(1)).Tick(ghost, new MonsterAiTuning(ghost.Def), prey, 500f);
        Assert.True(ghost.AiState != MonsterAiState.Attack && ghost.TargetId == 0, "a charmed monster attacked its caster");
        return Task.CompletedTask;
    }

    private static Task RoarAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var field = SkillFixtures.Field(37, definition);
        var fighter = SkillFixtures.Player(field, 1, PlayerClass.Fighter, 80, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Axe));
        fighter.Character.Skills.Restore(0x02020003, 10);
        fighter.Character.Skills.Restore(0x02010004, 10);
        var near = SkillFixtures.Spawn(field, definition, new Vector3(60, 0, 0));
        var far = SkillFixtures.Spawn(field, definition, new Vector3(400, 0, 0));
        SkillFixtures.Accept("Roar", field, fighter, 0x02020003);
        SkillFixtures.Finish(field, fighter);
        Assert.True(near.Effects.Has(ControlFlags.Stun) && !far.Effects.Has(ControlFlags.Stun), "roar stunned the wrong enemies");
        for (var attempt = 0; attempt < 5 && near.Life == near.Def.Combat.MaxLife; attempt++)
        {
            SkillFixtures.Ready(field, fighter);
            near.Position = new Vector3(40, 0, 0);
            SkillFixtures.Accept("Impact", field, fighter, 0x02010004, near.EntityId);
            SkillFixtures.Finish(field, fighter);
        }

        Assert.True(near.Life < near.Def.Combat.MaxLife, "the attack on the stunned enemy never landed");
        Assert.True(!near.Effects.Has(ControlFlags.Stun), "damage did not break the stun");
        return Task.CompletedTask;
    }

    private static Task PoisonAsync()
    {
        var definition = SkillFixtures.Monster(life: 5000);
        var field = SkillFixtures.Field(41, definition);
        var pikeman = SkillFixtures.Player(field, 1, PlayerClass.Pikeman, 80, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Pole, 1, 1));
        pikeman.Character.Skills.Restore(0x03040001, 10);
        var target = SkillFixtures.Spawn(field, definition, new Vector3(60, 0, 0));
        SkillFixtures.Accept("Venom Spear", field, pikeman, 0x03040001);
        SkillFixtures.Finish(field, pikeman);
        Assert.True(target.Effects.TryGet(0x03040001, out var poison) && poison.Spec.Periodic.Length == 1, "the poison was not applied");
        var afterHit = target.Life;
        SkillFixtures.Wait(field, 2f);
        Assert.True(target.Life < afterHit, "the poison did not drain life");
        target.Life = 20;
        SkillFixtures.Wait(field, 6f);
        Assert.True(target.IsAlive && target.Life >= 1, "the poison killed its target");
        return Task.CompletedTask;
    }

    private static Task TauntAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var field = SkillFixtures.Field(43, definition);
        var mechanic = SkillFixtures.Player(field, 1, PlayerClass.Mechanician, 80, Vector3.Zero);
        var other = SkillFixtures.Player(field, 2, PlayerClass.Fighter, 80, new Vector3(0, 0, 300));
        mechanic.Character.Skills.Restore(0x01080002, 10);
        var monster = SkillFixtures.Spawn(field, definition, new Vector3(100, 0, 0));
        monster.TargetId = other.EntityId;
        var absorption = mechanic.Character.Derived.Absorption;
        SkillFixtures.Accept("Compulsion", field, mechanic, 0x01080002);
        SkillFixtures.Finish(field, mechanic);
        Assert.True(monster.TargetId == mechanic.EntityId && monster.TargetLockMsLeft > 0f, "the enemy did not turn on the caster");
        Assert.True(mechanic.Character.Derived.Absorption == absorption + 23, "compulsion did not harden the caster");
        return Task.CompletedTask;
    }

    private static Task SlowAsync()
    {
        var definition = SkillFixtures.Monster(life: 100_000);
        var icy = SkillFixtures.Monster(life: 100_000, resistances: new ResistanceBlock(0, 0, 0, 50, 0, 0, 0, 0)) with { Code = 12 };
        var field = SkillFixtures.Field(17, definition, icy);
        var priest = SkillFixtures.Player(field, 1, PlayerClass.Priestess, 70, Vector3.Zero, SkillFixtures.Weapon(ItemKind.Staff));
        priest.Character.Skills.Restore(0x07080001, 10);
        var plain = SkillFixtures.Spawn(field, definition, new Vector3(0, 0, 150));
        var resistant = SkillFixtures.Spawn(field, icy, new Vector3(0, 0, 200));
        SkillFixtures.Accept("Glacial Spike", field, priest, 0x07080001, plain.EntityId);
        SkillFixtures.Finish(field, priest);
        Assert.True(plain.Effects.TryGet(0x07080001, out var chill) && plain.Effects.Totals.Percent(StatKind.MoveSpeed) == -22 && chill.DurationMs == 8000f,
            "Glacial Spike chill on the plain monster");
        Assert.True(resistant.Effects.TryGet(0x07080001, out var halved) && halved.DurationMs == 4000f, "ice resistance did not shorten the chill");

        var mage = SkillFixtures.Player(field, 2, PlayerClass.Magician, 70, new Vector3(0, 0, 160), SkillFixtures.Weapon(ItemKind.Staff));
        mage.Character.Skills.Restore(0x08080003, 10);
        SkillFixtures.Accept("Distortion", field, mage, 0x08080003);
        SkillFixtures.Finish(field, mage);
        Assert.True(plain.Effects.TryGet(0x08080003, out var distortion) && distortion.DurationMs == 22000f
            && plain.Effects.Totals.Percent(StatKind.MoveSpeed) <= -27, "Distortion slow");
        return Task.CompletedTask;
    }

    private sealed class FieldPreyFixture(ulong id, Vector3 position) : IPreyView
    {
        public IReadOnlyList<PreyCandidate> Candidates { get; } = [new PreyCandidate(id, position, true)];

        public bool TryGet(ulong entityId, out PreyCandidate prey)
        {
            prey = Candidates[0];
            return entityId == id;
        }
    }
}
