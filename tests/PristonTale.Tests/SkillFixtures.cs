using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Skills;
using PristonTale.Simulation.World.Ai;
using PristonTale.Simulation.World.Fields;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// Field scenarios for skill tests: a field with passive monsters, players of a class at a level with skills learned and
// a weapon equipped, and the steps that run a cast through its motion.
internal static class SkillFixtures
{
    private static readonly CharacterClassRegistry Classes = new();

    public static MonsterDefinition Monster(MonsterBrood brood = MonsterBrood.Normal, int life = 50000, int absorption = 0, int damage = 100,
        ResistanceBlock? resistances = null) => new(
        7, "Skill target", MonsterFaction.Enemy, MonsterClass.Normal, brood,
        new MonsterCombatStats(1, 5000, new MonsterRange(damage, damage), 6, 60, 0, absorption, 0, life, resistances ?? ResistanceBlock.Zero), "");

    public static FieldInstance Field(int seed = 11, params MonsterDefinition[] definitions) =>
        new(3, FieldSpawnData.Empty, new MonsterCatalog(definitions.Length > 0 ? definitions : [Monster()]), new EntityIdAllocator(), new CombatRng(seed));

    public static WorldPlayer Player(FieldInstance field, ulong id, PlayerClass playerClass, int level, Vector3 position, ItemInstance? weapon = null,
        ItemInstance? offHand = null)
    {
        var character = Classes.CreatePlayer(playerClass, "Skill" + id, level);
        if (weapon is not null)
        {
            character.Equip(EquipmentSlot.Weapon, weapon);
        }

        if (offHand is not null)
        {
            character.Equip(EquipmentSlot.OffHand, offHand);
        }

        var player = new WorldPlayer(id, position, character, false, default);
        field.Add(player);
        return player;
    }

    // Puts the players in one party, the first leading, as an accepted request would.
    public static Party Group(params WorldPlayer[] players)
    {
        var party = new Party(players[0].EntityId, players[1].EntityId);
        foreach (var player in players.Skip(2))
        {
            party.Add(player.EntityId);
        }

        foreach (var player in players)
        {
            player.Party = party;
        }

        return party;
    }

    public static MonsterInstance Spawn(FieldInstance field, MonsterDefinition definition, Vector3 position) =>
        ((ISpawnSink)field).Spawn(definition, position, 0, 0);

    public static SkillCastOutcome Cast(FieldInstance field, WorldPlayer player, uint code, ulong target = 0) =>
        field.CastSkill(player, new SkillCastRequest(1, code, target, default, false));

    // Scenario casts start settled with full mana and stamina, so they test the mechanic rather than the caster's pools.
    public static void Accept(string skill, FieldInstance field, WorldPlayer player, uint code, ulong target = 0)
    {
        Ready(field, player);
        var outcome = Cast(field, player, code, target);
        Assert.True(outcome.Accepted, $"{skill} refused: {outcome.Verdict}");
    }

    public static void Refuse(SkillCastVerdict expected, string failure, FieldInstance field, WorldPlayer player, uint code, ulong target = 0)
    {
        Ready(field, player);
        var outcome = Cast(field, player, code, target);
        Assert.True(outcome.Verdict == expected, $"{failure}: {outcome.Verdict}");
    }

    public static void Ready(FieldInstance field, WorldPlayer player)
    {
        for (var tick = 0; tick < 200 && player.IsBusy; tick++)
        {
            field.Step(0.05f);
        }

        player.Character.Mana = player.Character.MaxMana;
        player.Character.Stamina = player.Character.MaxStamina;
    }

    // A normal attack of the monster standing beside the player (monsters wander between the steps of a scenario).
    public static void Strike(FieldInstance field, MonsterInstance monster, WorldPlayer player)
    {
        monster.Position = player.Position + new Vector3(40, 0, 0);
        field.Combat.ResolveMonsterHit(monster, new MonsterAttackEvent(monster.EntityId, player.EntityId, false));
    }

    // Steps the field until the caster's motion is over (and its projectiles have landed).
    public static void Finish(FieldInstance field, WorldPlayer player, int maxTicks = 400)
    {
        for (var tick = 0; tick < maxTicks && player.IsCasting; tick++)
        {
            field.Step(0.05f);
        }

        for (var tick = 0; tick < 20; tick++)
        {
            field.Step(0.05f);
        }
    }

    // Finish, collecting the combat events of the steps.
    public static List<CombatEventView> FinishCollect(FieldInstance field, WorldPlayer player, int maxTicks = 400)
    {
        var events = new List<CombatEventView>();
        for (var tick = 0; tick < maxTicks && (player.IsCasting || tick < 20); tick++)
        {
            field.Step(0.05f);
            events.AddRange(field.SnapshotEvents() ?? []);
        }

        for (var tick = 0; tick < 20; tick++)
        {
            field.Step(0.05f);
            events.AddRange(field.SnapshotEvents() ?? []);
        }

        return events;
    }

    public static void Wait(FieldInstance field, float seconds)
    {
        for (var tick = 0; tick < seconds / 0.05f; tick++)
        {
            field.Step(0.05f);
        }
    }

    public static ItemInstance Weapon(ItemKind kind, int min = 10, int max = 20) => new(new ItemDefinition(0x0100_0100u + (uint)kind, kind.ToString(), kind,
        EquipmentSlot.Weapon)
    {
        CatalogKey = "W" + kind,
        BaseStats = new ItemStatBlock(min, max, 0, 0, 0, 0, 0, 0, 0, 0),
        ShootingRange = kind is ItemKind.Staff or ItemKind.ThrowingWeapon or ItemKind.Bow ? 200 : 0
    });

    public static ItemInstance Shield() => new(new ItemDefinition(0x0100_0011, "Shield", ItemKind.Shield, EquipmentSlot.OffHand) { CatalogKey = "DS101" });
}
