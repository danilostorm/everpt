using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Motion;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// A one-field world for monster skill cases: synthetic monsters sharing one model ("fixture.inx" with an attack, a skill,
// an eating and a transformation clip), players placed by the case and 50 ms steps that keep every event and status.
internal sealed class MonsterSkillField
{
    public const float Tick = 0.05f;

    private static readonly CharacterClassRegistry Classes = new();

    public MonsterSkillField(params MonsterDefinition[] definitions)
        : this(Timing(), definitions)
    {
    }

    public MonsterSkillField(MotionTimingSet timing, params MonsterDefinition[] definitions)
    {
        var numbered = definitions.Select((definition, index) => definition with { Code = index + 1 }).ToArray();
        Definitions = numbered;
        var motions = new MotionTimingCatalog(new Dictionary<string, MotionTimingSet> { ["fixture.inx"] = timing });
        Field = new FieldInstance(7, FieldSpawnData.Empty, new MonsterCatalog(numbered), new EntityIdAllocator(), new CombatRng(5), motions);
    }

    public FieldInstance Field { get; }
    public IReadOnlyList<MonsterDefinition> Definitions { get; }
    public List<CombatEventView> Events { get; } = new();
    public List<StatusEventView> Statuses { get; } = new();

    // Attack clip: 30 frames, blow at frame 10 (slot 2). Skill clips: 30 frames, blow at frame 10 (slot 3, key J) and
    // 60 frames with blows at 20 and 40 (slot 4, key K). Eating: 20 frames; transformation: 30 frames.
    public static MotionTimingSet Timing(bool twoSkills = false)
    {
        var clips = new List<KeyValuePair<int, MotionClip>>
        {
            new((int)MotionCode.Attack, new MotionClip(0, 30, 1600, 0, 0, 0, false) { Slot = 2, Key = 'A' }),
            new((int)MotionCode.Skill, new MotionClip(100, 130, 1600, 0, 0, 0, false) { Slot = 3, Key = 'J' }),
            new((int)MotionCode.Eat, new MotionClip(200, 220, 0, 0, 0, 0, false) { Slot = 5 }),
            new((int)MotionCode.Warp, new MotionClip(300, 330, 0, 0, 0, 0, false) { Slot = 6 })
        };
        if (twoSkills)
        {
            clips.Add(new((int)MotionCode.Skill, new MotionClip(400, 460, 3200, 6400, 0, 0, false) { Slot = 4, Key = 'K' }));
        }

        return MotionTimingSet.Build(clips);
    }

    public static MonsterDefinition Monster(int soundCode, int level = 50, int skillMin = 30, int skillMax = 30, int range = 0, int distance = 0,
        int rating = 100, int curse = 0, int life = 10000, int attack = 10) =>
        new(0, $"Monster {soundCode:X}", MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Normal,
            new MonsterCombatStats(level, 5000, new MonsterRange(attack, attack), 6, 80, 0, 0, 0, life, ResistanceBlock.Zero), "fixture.inx")
        {
            SoundCode = soundCode,
            Skill = new MonsterSkillStats(new MonsterRange(skillMin, skillMax), range, distance, rating, curse),
            Ai = new MonsterAi(6, MonsterNature.Evil, 1000, 1, 64, MonsterRange.Zero, MonsterActiveHour.Any)
        };

    public WorldPlayer AddPlayer(ulong id, Vector3 position, PlayerClass playerClass = PlayerClass.Fighter, int level = 100)
    {
        var character = Classes.CreatePlayer(playerClass, $"Player {id}", level);
        var player = new WorldPlayer(id, position, character, false, default);
        Field.Add(player);
        return player;
    }

    public MonsterInstance Spawn(MonsterDefinition definition, Vector3 position, ulong target = 0, int point = 0)
    {
        var monster = ((ISpawnSink)Field).Spawn(Definitions.First(d => d.Name == definition.Name && d.Combat.Level == definition.Combat.Level),
            position, point, 0f);
        if (target != 0)
        {
            monster.TargetId = target;
            monster.AiState = MonsterAiState.Chase;
        }

        return monster;
    }

    public void Step(int ticks = 1)
    {
        for (var index = 0; index < ticks; index++)
        {
            Field.Step(Tick);
            Events.AddRange(Field.SnapshotEvents() ?? []);
            Statuses.AddRange(Field.SnapshotStatusEvents());
        }
    }

    // Steps until the condition holds; fails after the limit.
    public void StepUntil(Func<bool> condition, int limit, string failure)
    {
        for (var index = 0; index < limit; index++)
        {
            if (condition())
            {
                return;
            }

            Step();
        }

        Assert.True(condition(), failure);
    }

    public IEnumerable<CombatEventView> Swings(ulong attacker) =>
        Events.Where(value => value.Kind == CombatEventKind.SwingBegin && value.AttackerId == attacker);

    public IEnumerable<CombatEventView> HitsOn(ulong target) =>
        Events.Where(value => value.Kind == CombatEventKind.Hit && value.TargetId == target);

    public MonsterInstance? MonsterOf(int soundCode) => ((IFieldSkillHost)Field).Monsters.FirstOrDefault(m => m.Def.SoundCode == soundCode && m.IsAlive);

    public IEnumerable<MonsterInstance> Monsters => ((IFieldSkillHost)Field).Monsters;
}
