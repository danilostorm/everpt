using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// What monster skills and blows leave on players: slows, poison, stuns, drains, curses, and the power one monster lends
// another.
internal static class MonsterSkillStatusTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("ice skills slow the struck player for a time its ice resistance shortens", IceSlowAsync);
        yield return ("poison skills drain life over time without killing", PoisonAsync);
        yield return ("stunning skills hold the player: no swing, no walk", StunAsync);
        yield return ("chaos cara drains mana with every skill but its J clip", ManaDrainAsync);
        yield return ("web and king bat blows slow and poison, succubus blows drain stamina or mana by level", BlowRidersAsync);
        yield return ("tower crystals burn through fire resistance and stun through lightning unless already stunned", CrystalsAsync);
        yield return ("defence and attack curses weaken the cursed player for ten seconds", CursesAsync);
        yield return ("a succubus lends a nearby monster power when its clip ends, or attacks when none is near", AllyPowerAsync);
        yield return ("curse monsters forget their player after each normal blow", CurseTargetAsync);
    }

    private static MonsterInstance CastOnce(MonsterSkillField world, MonsterDefinition definition, ulong target)
    {
        var monster = world.Spawn(definition, Vector3.Zero, target);
        world.StepUntil(() => world.Events.Any(value => value.Kind == CombatEventKind.MonsterSkillImpact && value.AttackerId == monster.EntityId), 40,
            "the skill never landed");
        return monster;
    }

    private static Task IceSlowAsync()
    {
        foreach (var (sound, frames) in new[] { (CharacterSoundCodes.ColdEye, MonsterStatuses.ColdSlowFrames), (CharacterSoundCodes.Frost, MonsterStatuses.FrostSlowFrames) })
        {
            var definition = MonsterSkillField.Monster(sound);
            var world = new MonsterSkillField(definition);
            var player = world.AddPlayer(1, new Vector3(50, 0, 0));
            var speed = player.Character.Derived.MoveSpeed;
            CastOnce(world, definition, 1);
            Assert.True(player.Character.Effects.TryGet(MonsterStatuses.IceSlow, out var slow), "ice skill did not slow");
            Assert.Near(MonsterStatuses.FramesMs(frames), slow.DurationMs, 1f);
            Assert.True(player.Character.Effects.Has(ControlFlags.Slow) && player.Character.Derived.MoveSpeed < speed, "slow did not lower the move speed");
            Assert.True(world.Statuses.Any(value => value.StatusCode == MonsterStatuses.IceSlow && value.SourceKind == (byte)EffectSourceKind.MonsterSkill),
                "the slow was not reported as a monster skill status");
        }

        Assert.True(MonsterStatuses.Resisted(280, 50) == 140 && MonsterStatuses.Resisted(280, 100) == 4 && MonsterStatuses.Resisted(280, -20) == 280,
            "resistance did not shorten the duration as the original");
        return Task.CompletedTask;
    }

    private static Task PoisonAsync()
    {
        var definition = MonsterSkillField.Monster(CharacterSoundCodes.Stingray, skillMin: 0, skillMax: 0);
        var world = new MonsterSkillField(definition);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        var monster = CastOnce(world, definition, 1);
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.Poison, out var poison), "stingray skill did not poison");
        Assert.Near(5000, poison.DurationMs, 1f);
        monster.ApplyDamage(monster.Life);
        var before = player.Life;
        world.Step(20);
        Assert.True(player.Life < before && before - player.Life <= 7 * 6, "poison did not drain life at its rate");
        player.ApplyDamage(player.Life - 2);
        world.Step(80);
        Assert.True(player.Life >= 1 && !player.IsDead, "poison killed the player");
        return Task.CompletedTask;
    }

    private static Task StunAsync()
    {
        foreach (var sound in new[] { CharacterSoundCodes.BloodyKnight, CharacterSoundCodes.Chimera })
        {
            var definition = MonsterSkillField.Monster(sound);
            var world = new MonsterSkillField(definition);
            var player = world.AddPlayer(1, new Vector3(50, 0, 0));
            var monster = CastOnce(world, definition, 1);
            Assert.True(player.Character.Effects.TryGet(MonsterStatuses.Stun, out var stun) && player.Character.Effects.Has(ControlFlags.Stun),
                "skill did not stun");
            Assert.Near(3000, stun.DurationMs, 1f);
            var at = player.Position;
            var verdict = player.ReportMovement(at + new Vector3(30, 0, 0), 0f, true, false, 0, (long)world.Field.NowMs + 500, world.Field.Terrain);
            Assert.True(player.Position == at && verdict != MovementVerdict.Accepted, "a stunned player walked");
            world.Field.SetAttackTarget(1, monster.EntityId, true);
            world.Step();
            Assert.True(!world.Swings(1).Any(), "a stunned player swung");
        }

        return Task.CompletedTask;
    }

    private static Task ManaDrainAsync()
    {
        var definition = MonsterSkillField.Monster(CharacterSoundCodes.ChaosCara, skillMin: 40, skillMax: 40);
        var other = MonsterSkillField.Timing(twoSkills: true);
        foreach (var (key, drains) in new[] { ('J', false), ('K', true) })
        {
            var timing = Simulation.World.Motion.MotionTimingSet.Build(other.ClipsForState(MotionCode.Skill)
                .Where(clip => clip.Key == key).Select(clip => new KeyValuePair<int, Simulation.World.Motion.MotionClip>((int)MotionCode.Skill, clip)));
            var world = new MonsterSkillField(timing, definition);
            var player = world.AddPlayer(1, new Vector3(50, 0, 0), PlayerClass.Magician);
            var mana = player.Character.Mana;
            CastOnce(world, definition, 1);
            Assert.True(drains ? player.Character.Mana < mana - 30 : player.Character.Mana >= mana, $"chaos cara key {key} drained wrongly");
        }

        return Task.CompletedTask;
    }

    private static Task BlowRidersAsync()
    {
        var web = MonsterSkillField.Monster(CharacterSoundCodes.Web, rating: 0, attack: 50);
        var world = new MonsterSkillField(web);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        world.Spawn(web, Vector3.Zero, target: 1);
        world.StepUntil(() => world.HitsOn(1).Any(), 80, "web never hit");
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.WebSlow, out var slow), "web blow did not slow");
        Assert.Near(2000, slow.DurationMs, 1f);

        var bat = MonsterSkillField.Monster(CharacterSoundCodes.KingBat, rating: 0, attack: 50);
        world = new MonsterSkillField(bat);
        player = world.AddPlayer(1, new Vector3(50, 0, 0));
        world.Spawn(bat, Vector3.Zero, target: 1);
        world.StepUntil(() => world.HitsOn(1).Any(), 80, "king bat never hit");
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.Poison, out _), "king bat blow did not poison");

        foreach (var (level, mana) in new[] { (77, false), (82, true), (75, false) })
        {
            var succubus = MonsterSkillField.Monster(CharacterSoundCodes.Succubus, level: level, rating: 0, attack: 50);
            world = new MonsterSkillField(succubus);
            player = world.AddPlayer(1, new Vector3(50, 0, 0));
            player.Character.Mana = player.Character.MaxMana;
            player.Character.Stamina = player.Character.MaxStamina;
            world.Spawn(succubus, Vector3.Zero, target: 1);
            world.StepUntil(() => world.HitsOn(1).Any(), 80, "succubus never hit");
            var manaLost = player.Character.Mana < player.Character.MaxMana;
            var staminaLost = player.Character.Stamina < player.Character.MaxStamina;
            Assert.True(level <= 75 ? !manaLost && !staminaLost : mana ? manaLost && !staminaLost : staminaLost && !manaLost,
                $"succubus of level {level} drained the wrong resource");
        }

        return Task.CompletedTask;
    }

    private static Task CrystalsAsync()
    {
        var fire = MonsterSkillField.Monster(CharacterSoundCodes.CastleCrystalFire, skillMin: 100, skillMax: 100);
        var world = new MonsterSkillField(fire);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        CastOnce(world, fire, 1);
        var hit = world.HitsOn(1).Single(value => value.Skill);
        var expected = MonsterStatuses.Resisted(100, player.Character.Derived.Resistances[Simulation.Combat.Element.Fire]) - player.Character.Derived.Absorption;
        Assert.True(hit.Damage == Math.Max(1, expected), "fire crystal did not resist its power by fire");

        var lightning = MonsterSkillField.Monster(CharacterSoundCodes.CastleCrystalLightning, skillMin: 1, skillMax: 1);
        world = new MonsterSkillField(lightning);
        player = world.AddPlayer(1, new Vector3(50, 0, 0));
        world.Field.QueueItemEffect(1, MonsterStatuses.StunSpec(MonsterStatuses.Stun), 60000);
        world.Step();
        CastOnce(world, lightning, 1);
        Assert.True(!player.Character.Effects.TryGet(MonsterStatuses.LightningStun, out _), "lightning crystal stunned a stunned player");

        world = new MonsterSkillField(lightning);
        player = world.AddPlayer(1, new Vector3(50, 0, 0));
        CastOnce(world, lightning, 1);
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.LightningStun, out var stun), "lightning crystal did not stun");
        Assert.Near(3000, stun.DurationMs, 1f);
        return Task.CompletedTask;
    }

    private static Task CursesAsync()
    {
        var witch = MonsterSkillField.Monster(CharacterSoundCodes.Succubus, level: 71, skillMin: 0, skillMax: 0, rating: 100, curse: 101);
        var world = new MonsterSkillField(witch);
        var player = world.AddPlayer(1, new Vector3(50, 0, 0));
        var absorption = player.Character.Derived.Absorption;
        CastOnce(world, witch, 1);
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.CurseDefence, out var curse), "witch did not curse");
        Assert.Near(10000, curse.DurationMs, 1f);
        var lowered = absorption - player.Character.Derived.Absorption;
        Assert.True(lowered is >= 2 and <= 6, $"defence curse lowered absorption by {lowered}");
        Assert.True(world.HitsOn(1).Any(value => value.Skill && value.Damage == 1), "a curse skill without damage did not strike for 1");

        var mystic = MonsterSkillField.Monster(CharacterSoundCodes.Mystic, skillMin: 400, skillMax: 400, range: 140, rating: 100, curse: 101);
        world = new MonsterSkillField(mystic);
        player = world.AddPlayer(1, new Vector3(50, 0, 0));
        absorption = player.Character.Derived.Absorption;
        var life = player.Life;
        CastOnce(world, mystic, 1);
        lowered = absorption - player.Character.Derived.Absorption;
        Assert.True(lowered is >= 10 and <= 16 && player.Life == life, "mystic curse dealt damage or the wrong curse");

        var incubus = MonsterSkillField.Monster(CharacterSoundCodes.Succubus, level: 82, skillMin: 0, skillMax: 0, rating: 100, curse: 103);
        world = new MonsterSkillField(incubus);
        player = world.AddPlayer(1, new Vector3(50, 0, 0));
        CastOnce(world, incubus, 1);
        Assert.True(player.Character.Effects.TryGet(MonsterStatuses.CurseAttack, out _), "incubus did not curse attacks");
        var weakened = -player.Character.Effects.Totals.Percent(StatKind.DamageDealt);
        Assert.True(weakened is >= 10 and <= 15, $"attack curse weakened by {weakened}%");
        return Task.CompletedTask;
    }

    private static Task AllyPowerAsync()
    {
        var succubus = MonsterSkillField.Monster(CharacterSoundCodes.Succubus, level: 75, skillMin: 0, skillMax: 0, rating: 100, curse: 102);
        var ally = MonsterSkillField.Monster(0x1000, rating: 0);
        var world = new MonsterSkillField(succubus, ally);
        world.AddPlayer(1, new Vector3(50, 0, 0));
        var friend = world.Spawn(ally, new Vector3(-100, 0, 0));
        var caster = world.Spawn(succubus, Vector3.Zero, target: 1);
        world.StepUntil(() => world.Swings(caster.EntityId).Any(), 5, "succubus never swung");
        var swing = world.Swings(caster.EntityId).First();
        Assert.True(swing.Skill && swing.TargetId == friend.EntityId, "the power skill did not aim at the ally");
        world.StepUntil(() => friend.Effects.TryGet(MonsterStatuses.Power, out _), 40, "the ally never received power");
        friend.Effects.TryGet(MonsterStatuses.Power, out var power);
        Assert.Near(MonsterStatuses.PowerMs, power.DurationMs, 1f);
        Assert.True(friend.Effects.Totals.Percent(StatKind.DamageDealt) is >= 10 and <= 20, "lent power out of range");

        world = new MonsterSkillField(succubus);
        world.AddPlayer(1, new Vector3(50, 0, 0));
        caster = world.Spawn(succubus, Vector3.Zero, target: 1);
        world.StepUntil(() => world.Swings(caster.EntityId).Any(), 5, "succubus never swung");
        Assert.True(!world.Swings(caster.EntityId).First().Skill && caster.Motion == MotionState.Attack, "without an ally the swing stayed a skill");
        return Task.CompletedTask;
    }

    private static Task CurseTargetAsync()
    {
        var witch = MonsterSkillField.Monster(CharacterSoundCodes.Succubus, level: 71, skillMin: 0, skillMax: 0, rating: 0, curse: 101);
        var world = new MonsterSkillField(witch);
        world.AddPlayer(1, new Vector3(50, 0, 0));
        var monster = world.Spawn(witch, Vector3.Zero, target: 1);
        world.StepUntil(() => world.Swings(monster.EntityId).Any(), 5, "witch never swung");
        world.StepUntil(() => monster.AiState != MonsterAiState.Attack, 40, "blow never ended");
        Assert.True(monster.TargetId == 0, "curse monster kept its player after a blow");
        return Task.CompletedTask;
    }
}
