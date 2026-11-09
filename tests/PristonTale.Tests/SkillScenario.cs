using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// A field with one player and hand-made monsters for server skill scenarios, stepped at the 50 ms server tick.
internal sealed class SkillScenario
{
    public const float Tick = 0.05f;

    private static readonly CharacterClassRegistry Classes = new();

    private SkillScenario(FieldInstance field, WorldPlayer player)
    {
        Field = field;
        Player = player;
    }

    public FieldInstance Field { get; }
    public WorldPlayer Player { get; }
    public PlayerCharacter Character => Player.Character;
    public IFieldSkillHost Host => Field;
    public List<CombatEventView> Combat { get; } = new();
    public List<SkillEventView> Skills { get; } = new();
    public List<StatusEventView> Statuses { get; } = new();

    public static MonsterDefinition Enemy(string name = "Skill target", int life = 50000, int sight = 0, int level = 20, int damage = 1, int defence = 30,
        MonsterBrood brood = MonsterBrood.Normal) =>
        new(1, name, MonsterFaction.Enemy, MonsterClass.Normal, brood,
            new MonsterCombatStats(level, 400, new MonsterRange(damage, damage), 6, 40, defence, 0, 0, life, ResistanceBlock.Zero), "")
        {
            Ai = new MonsterAi(6, MonsterNature.Evil, sight, 1, 2000, MonsterRange.Zero, MonsterActiveHour.Any)
        };

    // A catalog monster that a summon skill names: it gives the model, reach, sight and attack timing.
    public static MonsterDefinition SummonModel(string name, int code) =>
        new(code, name, MonsterFaction.Enemy, MonsterClass.Normal, MonsterBrood.Mechanic,
            new MonsterCombatStats(60, 300, new MonsterRange(10, 20), 6, 50, 100, 0, 0, 1000, ResistanceBlock.Zero), "")
        {
            Ai = new MonsterAi(8, MonsterNature.Natural, 300, 2, 0, MonsterRange.Zero, MonsterActiveHour.Any)
        };

    public static SkillScenario Create(PlayerClass playerClass, int level, ItemInstance? weapon = null, ItemInstance? offHand = null,
        params MonsterDefinition[] catalog)
    {
        var field = new FieldInstance(3, FieldSpawnData.Empty, new MonsterCatalog(catalog), new EntityIdAllocator(), new CombatRng(11));
        var character = Classes.CreatePlayer(playerClass, "Caster", level);
        foreach (var item in new[] { weapon, offHand })
        {
            if (item is not null)
            {
                character.Equip(item.Definition.EquipSlot!.Value, item);
            }
        }

        character.Mana = character.MaxMana;
        character.Stamina = character.MaxStamina;
        var player = new WorldPlayer(1, Vector3.Zero, character, false, default);
        field.Add(player);
        return new SkillScenario(field, player);
    }

    public MonsterInstance Spawn(MonsterDefinition definition, Vector3 position) => ((ISpawnSink)Field).Spawn(definition, position, 0, 0);

    public SkillScenario Learn(uint code, int rank)
    {
        Character.Skills.Restore(code, rank);
        return this;
    }

    public SkillCastOutcome Cast(uint code, ulong targetId = 0, SkillCastCommand command = SkillCastCommand.Cast) =>
        Field.CastSkill(Player, new SkillCastRequest(1, code, targetId, default, false, command));

    // Steps the field, keeping the snapshot events of every tick; stops early once until holds.
    public int Run(int ticks, Func<bool>? until = null)
    {
        for (var tick = 1; tick <= ticks; tick++)
        {
            Field.Step(Tick);
            Combat.AddRange(Field.SnapshotEvents() ?? []);
            Skills.AddRange(Field.SnapshotSkillEvents());
            Statuses.AddRange(Field.SnapshotStatusEvents());
            if (until?.Invoke() == true)
            {
                return tick;
            }
        }

        return -1;
    }

    public void Refill()
    {
        Character.Life = Character.MaxLife;
        Character.Mana = Character.MaxMana;
        Character.Stamina = Character.MaxStamina;
    }

    public void ClearCooldowns() => Run(20 * 20);

    public IEnumerable<MonsterInstance> Summons => Host.Monsters.Where(monster => monster.IsSummon && monster.IsAlive);

    public static ItemInstance Weapon(ItemKind kind, string key, int min = 20, int max = 30) =>
        new(new ItemDefinition(0x0100_0100 + (uint)kind, kind.ToString(), kind, EquipmentSlot.Weapon)
        {
            CatalogKey = key,
            BaseStats = new ItemStatBlock(min, max, 0, 0, 0, 0, 0, 0, 0, 0)
        });

    public static ItemInstance Shield() => new(new ItemDefinition(0x0100_0011, "Shield", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101" });
}
