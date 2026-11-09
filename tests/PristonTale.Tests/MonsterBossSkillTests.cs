using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Modifiers;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Monsters.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Simulation.World.Npcs;

namespace PristonTale.Tests;

// Boss phases and the monsters that change the fight: Babel's double blow, splash and regeneration, virtual lives and
// the reinforcements they call, potions, shape changes, the vampiric machine's bees, the mystic's explosion, Ratoo's
// wake-up and the bosses' blows on summons.
internal static class MonsterBossSkillTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("babel strikes twice per blow, splashes the players around its target and regenerates", BabelAsync);
        yield return ("valento, kelvezu and mokova spend virtual lives and call reinforcements every second life", VirtualLivesAsync);
        yield return ("hurt monsters drink their potions and heal half their life", PotionsAsync);
        yield return ("shape-shifters turn into their other form when hurt or angry", TransformAsync);
        yield return ("the vampiric machine stands and sends bees until four stand on its point", VampiricMachineAsync);
        yield return ("the mystic explodes on death and by itself after its lifetime", MysticAsync);
        yield return ("ratoo's skill wakes the stunned monsters around it", RatooAsync);
        yield return ("great bosses deal eight times their roll to monsters, wild on summons or summoned on wild ones, and kelvezu kills them", BossesOnSummonsAsync);
    }

    private static Task BabelAsync()
    {
        var babel = MonsterSkillField.Monster(CharacterSoundCodes.Babel, rating: 0, attack: 2, life: 1_000_000);
        var timing = Simulation.World.Motion.MotionTimingSet.Build([
            new((int)MotionCode.Attack, new Simulation.World.Motion.MotionClip(0, 55, 2880, 5280, 0, 0, false) { Slot = 2 })
        ]);
        var world = new MonsterSkillField(timing, babel);
        world.AddPlayer(1, new Vector3(50, 0, 0), level: 1);
        world.AddPlayer(2, new Vector3(80, 0, 40), level: 1);
        world.AddPlayer(3, new Vector3(400, 0, 0), level: 1);
        var monster = world.Spawn(babel, Vector3.Zero, target: 1);
        world.StepUntil(() => world.Swings(monster.EntityId).Count() >= 30, 2000, "babel stopped swinging");
        var swings = world.Swings(monster.EntityId).Count();
        var blows = world.Events.Count(value => value.AttackerId == monster.EntityId && value.TargetId == 1
            && value.Kind is CombatEventKind.Hit or CombatEventKind.Block);
        Assert.True(blows > swings * 1.3, $"babel struck {blows} times in {swings} swings");
        Assert.True(world.HitsOn(2).Any(), "babel never splashed the player beside its target");
        Assert.True(!world.HitsOn(3).Any(), "babel splashed a player far from its target");

        monster.Life = 1000;
        var life = monster.Life;
        world.StepUntil(() => monster.Life > life, 200, "babel never regenerated");
        Assert.True(MonsterLifeRules.BabelRegen(3, 900, 1000) == 1800 && MonsterLifeRules.BabelRegen(8, 100, 1000) == 2550
            && MonsterLifeRules.BabelRegen(40, 900, 1000) == 2800, "babel regeneration table");
        return Task.CompletedTask;
    }

    private static Task VirtualLivesAsync()
    {
        var kelvezu = MonsterSkillField.Monster(CharacterSoundCodes.Kelvezu, rating: 0, life: 1000) with { PotionCount = 4 };
        var chaos = MonsterSkillField.Monster(CharacterSoundCodes.ChaosCara, level: 100, rating: 0);
        var greater = MonsterSkillField.Monster(CharacterSoundCodes.ChaosCara, level: 130, rating: 0);
        var world = new MonsterSkillField(kelvezu, chaos, greater);
        world.AddPlayer(1, new Vector3(500, 0, 0));
        var boss = world.Spawn(kelvezu, Vector3.Zero);
        Assert.True(boss.VirtualLives == 4 && FieldMonsterSkills.ViewLife(boss) == (500, 500), "virtual lives were not prepared");
        boss.ApplyDamage(5000);
        Assert.True(boss.IsAlive && boss.Life == 1000 && boss.VirtualLives == 3, "a lethal blow did not spend a life");
        world.Step();
        Assert.True(world.Monsters.Count(m => m.Def.SoundCode == CharacterSoundCodes.ChaosCara) == 0, "an odd life called reinforcements");
        boss.ApplyDamage(950);
        world.Step();
        var called = world.Monsters.Where(m => m.Def.SoundCode == CharacterSoundCodes.ChaosCara).ToList();
        Assert.True(called.Count == 2 && called.All(m => m.Def.Combat.Level == 100), "an even life did not call two ordinary chaos cara");
        Assert.True(FieldMonsterSkills.ViewLife(boss).Life == 200 + 100, "the life bar did not span the lives");
        boss.ApplyDamage(950);
        boss.ApplyDamage(950);
        boss.ApplyDamage(5000);
        Assert.True(!boss.IsAlive && boss.VirtualLives == 0, "the last life did not end the boss");

        var mokova = MonsterSkillField.Monster(CharacterSoundCodes.Mokova, rating: 0, life: 1000) with { PotionCount = 2 };
        var guard = MonsterSkillField.Monster(CharacterSoundCodes.DarkGuard, rating: 0);
        world = new MonsterSkillField(mokova, guard);
        world.AddPlayer(1, new Vector3(500, 0, 0));
        boss = world.Spawn(mokova, Vector3.Zero);
        boss.ApplyDamage(950);
        boss.ApplyDamage(950);
        world.Step();
        Assert.True(world.Monsters.Count(m => m.Def.SoundCode == CharacterSoundCodes.DarkGuard) == 2, "mokova did not call two dark guards");

        var ordinary = MonsterSkillField.Monster(0x1000, rating: 0) with { PotionCount = 4 };
        world = new MonsterSkillField(ordinary);
        var plain = world.Spawn(ordinary, Vector3.Zero);
        Assert.True(plain.VirtualLives == 0, "an ordinary monster received lives");
        return Task.CompletedTask;
    }

    private static Task PotionsAsync()
    {
        var drinker = MonsterSkillField.Monster(0x1000, rating: 0, life: 1000) with { PotionCount = 3, PotionPercent = 100 };
        var world = new MonsterSkillField(drinker);
        world.AddPlayer(1, new Vector3(50, 0, 0));
        var monster = world.Spawn(drinker, Vector3.Zero, target: 1);
        Assert.True(monster.Potions is >= 1 and <= 3, "spawn did not carry potions");
        var potions = monster.Potions;
        monster.Life = 200;
        world.StepUntil(() => monster.Motion == MotionState.Eat, 80, "a hurt monster never drank");
        Assert.True(monster.Potions == potions - 1, "drinking did not spend a potion");
        world.StepUntil(() => monster.Motion != MotionState.Eat, 40, "the drink never ended");
        Assert.True(monster.Life >= 700 && world.Events.Any(value => value.Kind == CombatEventKind.PotionApplied && value.AttackerId == monster.EntityId),
            "the potion did not heal half the life");

        var dry = MonsterSkillField.Monster(0x1000, rating: 0) with { PotionCount = 3, PotionPercent = 0 };
        world = new MonsterSkillField(dry);
        Assert.True(world.Spawn(dry, Vector3.Zero).Potions == 0, "a 0 % chance still carried potions");
        return Task.CompletedTask;
    }

    private static Task TransformAsync()
    {
        foreach (var angry in new[] { false, true })
        {
            var hungky = MonsterSkillField.Monster(CharacterSoundCodes.Hungky, rating: 0, life: 1000);
            var hulk = MonsterSkillField.Monster(CharacterSoundCodes.Hulk, level: 60, rating: 0);
            var typhoon = MonsterSkillField.Monster(CharacterSoundCodes.Hulk, level: 85, rating: 0);
            var world = new MonsterSkillField(hungky, hulk, typhoon);
            world.AddPlayer(1, new Vector3(50, 0, 0));
            var monster = world.Spawn(hungky, new Vector3(5, 0, 5), target: 1);
            if (angry)
            {
                monster.AttackAnger = MonsterTransformRules.AngerLimit + 1;
            }
            else
            {
                monster.Life = 250;
            }

            world.StepUntil(() => monster.Motion == MotionState.Warp, 80, "the shape-shifter never started changing");
            world.StepUntil(() => world.MonsterOf(CharacterSoundCodes.Hulk) is not null, 60, "the shape-shifter never changed");
            var changed = world.MonsterOf(CharacterSoundCodes.Hulk)!;
            Assert.True(changed.Def.Combat.Level == (angry ? 85 : 60) && changed.Life == changed.Def.Combat.MaxLife
                && Vector2.Distance(new Vector2(changed.Position.X, changed.Position.Z), new Vector2(5, 5)) < 1f,
                "the change chose the wrong form or place");
            Assert.True(world.MonsterOf(CharacterSoundCodes.Hungky) is null, "the old form stayed");
        }

        return Task.CompletedTask;
    }

    private static Task VampiricMachineAsync()
    {
        var machine = MonsterSkillField.Monster(CharacterSoundCodes.VampiricMachine, rating: 0);
        var bee = MonsterSkillField.Monster(CharacterSoundCodes.VampiricBee, rating: 0);
        var world = new MonsterSkillField(machine, bee);
        var player = world.AddPlayer(1, new Vector3(60, 0, 0));
        var monster = world.Spawn(machine, Vector3.Zero, target: 1, point: 3);
        var life = player.Life;
        world.Step(400);
        Assert.True(monster.Position == Vector3.Zero, "the machine moved");
        Assert.True(world.Monsters.Count(m => m.Def.SoundCode == CharacterSoundCodes.VampiricBee && m.SpawnPointIndex == 3) == 3, "the machine sent the wrong number of bees");
        Assert.True(!world.HitsOn(1).Any(value => value.AttackerId == monster.EntityId), "the machine struck instead of sending bees");
        return Task.CompletedTask;
    }

    private static Task MysticAsync()
    {
        var mystic = MonsterSkillField.Monster(CharacterSoundCodes.Mystic, skillMin: 400, skillMax: 400, range: 140, rating: 0, curse: 101);
        var world = new MonsterSkillField(mystic);
        var near = world.AddPlayer(1, new Vector3(100, 0, 0));
        var away = world.AddPlayer(2, new Vector3(300, 0, 0));
        var monster = world.Spawn(mystic, Vector3.Zero);
        monster.ApplyDamage(monster.Life);
        var nearLife = near.Life;
        var awayLife = away.Life;
        world.Step();
        Assert.True(near.Life < nearLife && away.Life == awayLife, "the explosion missed its range");
        world.Step(5);
        Assert.True(world.Events.Count(value => value.Kind == CombatEventKind.MonsterSkillImpact) == 1, "the explosion repeated");

        world = new MonsterSkillField(mystic);
        world.AddPlayer(1, new Vector3(800, 0, 0));
        monster = world.Spawn(mystic, Vector3.Zero);
        world.Step((int)(FieldMonsterSkills.MysticLifetimeMs / 50f) + 2);
        Assert.True(!monster.IsAlive, "the mystic outlived its lifetime");
        return Task.CompletedTask;
    }

    private static Task RatooAsync()
    {
        var ratoo = MonsterSkillField.Monster(CharacterSoundCodes.Ratoo, range: 70, distance: 110);
        var other = MonsterSkillField.Monster(0x1000, rating: 0);
        var world = new MonsterSkillField(ratoo, other);
        world.AddPlayer(1, new Vector3(50, 0, 0));
        var stunned = world.Spawn(other, new Vector3(-120, 0, 0));
        var distant = world.Spawn(other, new Vector3(-400, 0, 0));
        foreach (var monster in new[] { stunned, distant })
        {
            world.Field.QueueItemEffect(monster.EntityId, new EffectSpec(0x01020003, EffectSourceKind.PlayerSkill, true, [], control: ControlFlags.Stun), 60000);
        }

        world.Step();
        world.Spawn(ratoo, Vector3.Zero, target: 1);
        world.StepUntil(() => world.Swings(world.MonsterOf(CharacterSoundCodes.Ratoo)!.EntityId).Any(), 5, "ratoo never cast");
        Assert.True(!stunned.Effects.Has(ControlFlags.Stun) && distant.Effects.Has(ControlFlags.Stun), "ratoo woke the wrong monsters");
        return Task.CompletedTask;
    }

    private static Task BossesOnSummonsAsync()
    {
        var rng = new CombatRng(3);
        var summon = new MonsterInstance(50, MonsterSkillField.Monster(0x3020, rating: 0, life: 100000), Vector3.Zero, 0f, -1) { OwnerId = 1 };
        int Blow(int soundCode) => Enumerable.Range(0, 100)
            .Select(_ => FieldMonsterSkills.BlowOnMonster(rng, new MonsterInstance(51, MonsterSkillField.Monster(soundCode, attack: 100), Vector3.Zero, 0f, 0), summon, false))
            .First(damage => damage is not null)!.Value;
        Assert.True(Blow(CharacterSoundCodes.Babel) == 800 && Blow(0x1000) == 100 && Blow(CharacterSoundCodes.Kelvezu) == summon.Life,
            "babel did not deal eight times its roll, an ordinary monster was multiplied, or kelvezu did not kill");

        // The same blow from a player's summoned boss on a wild monster.
        var babel = MonsterSkillField.Monster(CharacterSoundCodes.Babel, rating: 0, attack: 100, life: 1_000_000) with { Key = "Babel" };
        var wild = MonsterSkillField.Monster(0x1000, rating: 0, attack: 1, life: 1_000_000);
        var world = new MonsterSkillField(babel, wild);
        var owner = world.AddPlayer(1, Vector3.Zero);
        Assert.True(world.Field.CallCrystalMonster(owner, "GP101", "Babel") is not null, "the pet was not called");
        var pet = world.MonsterOf(CharacterSoundCodes.Babel)!;
        var prey = world.Spawn(wild, new Vector3(60, 0, 0));
        world.StepUntil(() => world.HitsOn(prey.EntityId).Any(hit => hit.AttackerId == pet.EntityId), 400, "the summoned babel never struck");
        var struck = world.HitsOn(prey.EntityId).First(hit => hit.AttackerId == pet.EntityId);
        Assert.True(struck.Damage == 800, $"a summoned babel's blow on a wild monster dealt {struck.Damage}, not eight times its roll");
        return Task.CompletedTask;
    }
}
