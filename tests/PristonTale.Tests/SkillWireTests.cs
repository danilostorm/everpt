using PristonTale.Application.World;
using PristonTale.Client.Net.Presence;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

// The skill values the later tiers added keep their meaning between the server's views, the wire and the client.
internal static class SkillWireTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("skill verdicts, event flags and combat kinds keep their values on the wire and in the client", WireAsync);
        yield return ("cast commands, releases, charged, counter and mode flags, the owner's tier and summon owners round-trip", CommandsAsync);
    }

    private static Task WireAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var result = new SkillCastResultPacket(9, 0x06040002, SkillCastVerdict.AlreadyActive, 10, 0, 0, 0, 0, 0);
        Assert.True(SkillCastResultPacket.Read(buffer.AsSpan(0, result.Write(buffer))) == result, "already-active result round trip");
        SkillEventEntry[] events =
        [
            new(1, 5, 0x07080003, SkillEventPhase.Impact, 3, SkillEventFlags.Chain, 0f, 0f, 128, 0, 0, 0, 0, 120),
            new(1, 6, 0x08040004, SkillEventPhase.Impact, 1, SkillEventFlags.Companion, 0f, 0f, 0, 0, 0, 0, 0, 0),
            new(1, 2, 0x07040001, SkillEventPhase.Impact, 10, SkillEventFlags.Ally | SkillEventFlags.SelfCast, 0f, 0f, 0, 0, 0, 0, 0, 0)
        ];
        var length = SkillEventList.Write(buffer, events, NoEventHandles.Instance, out _);
        Assert.True(SkillEventList.Read(buffer.AsSpan(0, length), NoEventHandles.Instance).SequenceEqual(events), "chain, companion and ally events round trip");
        CombatEventEntry[] combat =
        [
            new(4, 1, CombatEventKind.Evade, 0, 0f, 0f, 0, 0, 0, 0, 0, 0, 0),
            new(1, 2, CombatEventKind.Revive, 0, 0f, 0f, 840, 0, 0, 128, 0, -224, 0)
        ];
        length = CombatEventList.Write(buffer, combat, NoEventHandles.Instance, out _);
        Assert.True(CombatEventList.Read(buffer.AsSpan(0, length), NoEventHandles.Instance).SequenceEqual(combat), "evade and revive events round trip");
        return Task.CompletedTask;
    }

    private static Task CommandsAsync()
    {
        var buffer = new byte[FrameCodec.MaxFrameSize];
        foreach (var flags in new[] { SkillCastCommand.Cast, SkillCastCommand.Release, SkillCastCommand.SummonPassive, SkillCastCommand.SummonAggressive })
        {
            var cast = new SkillCastPacket(3, 0x03080002, 77, 256, 0, -512, 1024, 4000, flags);
            Assert.True(SkillCastPacket.Read(buffer.AsSpan(0, cast.Write(buffer))) == cast, $"cast command {flags} round trip");
        }

        var length = new SkillCastPacket(3, 0x03080002, 77, 0, 0, 0, 0, 0, (SkillCastCommand)4).Write(buffer);
        Assert.Throws<InvalidDataException>(() => SkillCastPacket.Read(buffer.AsSpan(0, length).ToArray()), "a malformed SkillCastPacket was read");
        length = new SkillCastPacket(3, 0x03080002, 77, 0, 0, 0, 0, 0).Write(buffer);
        BitConverter.TryWriteBytes(buffer.AsSpan(8), (ushort)2);
        Assert.Throws<InvalidDataException>(() => SkillCastPacket.Read(buffer.AsSpan(0, length).ToArray()), "a malformed SkillCastPacket was read");
        var wide = new SkillCastPacket(3, 0x03080002, 0x1_0000_0001UL, 0, 0, 0, 0, 0);
        Assert.True(SkillCastPacket.Read(buffer.AsSpan(0, wide.Write(buffer))).TargetId == 0x1_0000_0001UL, "64-bit cast target round trip");

        var mode = SkillEventFlags.None.WithMode(2);
        SkillEventEntry[] events =
        [
            new(1, 5, 0x03080002, SkillEventPhase.Release, 5, 0, 900f, 120f, 0, 0, 0, 0, 0, 0),
            new(1, 5, 0x03080002, SkillEventPhase.Impact, 5, SkillEventFlags.Charged, 0f, 0f, 0, 0, 0, 0, 1, 0),
            new(1, 6, 0x01040004, SkillEventPhase.Impact, 3, SkillEventFlags.Counter, 0f, 0f, 0, 0, 0, 0, 1, 0),
            new(1, 6, 0x04040001, SkillEventPhase.Impact, 4, SkillEventFlags.Area | mode, 0f, 0f, 0, 0, 0, 0, 2, 75)
        ];
        length = SkillEventList.Write(buffer, events, NoEventHandles.Instance, out _);
        Assert.True(SkillEventList.Read(buffer.AsSpan(0, length), NoEventHandles.Instance).SequenceEqual(events), "release, charged, counter and mode events round trip");
        buffer[EventWire.HeaderSize] = 5;
        Assert.Throws<InvalidDataException>(() => SkillEventList.Read(buffer.AsSpan(0, length).ToArray(), NoEventHandles.Instance), "a malformed SkillEventList was read");

        var classes = new Simulation.Characters.CharacterClassRegistry();
        var character = classes.CreatePlayer(PlayerClass.Archer, "Tier", 80);
        character.RestoreProgress(character.Experience, 1000, tier: 3, skillPoints: 4, eliteSkillPoints: 7);
        var status = Server.Net.WireMapping.Status(Simulation.Characters.OwnerState.Capture(character));
        var read = OwnerStatusPacket.Read(buffer.AsSpan(0, status.Write(buffer)));
        Assert.True(read == status && read.Tier == 3 && read.EliteSkillPoints == 7, "the owner's tier or final-tier points were lost");

        var golem = SkillScenario.SummonModel("Metal Golem", 40);
        var empty = WorldData.Empty;
        var data = new WorldDataHolder
        {
            Current = new WorldData(empty.Items, new Simulation.World.Monsters.MonsterCatalog([golem]), empty.Npcs, empty.FieldSpawns, empty.Motions)
        };
        var round = new WireRoundTrip(1, data);
        var summon = new EntityView(9, EntityKind.Monster, false, System.Numerics.Vector3.Zero, 0, MotionState.Stand, 60, 100, 100, default,
            templateId: 40) { OwnerId = 1, ModelScale = 1.25f };
        var self = new EntityView(1, EntityKind.Player, true, System.Numerics.Vector3.Zero, 0, MotionState.Stand, 80, 100, 100, default);
        var spawn = round.Push(new WorldSnapshot(5, [self, summon], fieldId: 3)).SelectMany(update => update.Spawns).Single(value => value.EntityId == 9);
        Assert.True(spawn.OwnerId == 1 && Math.Abs(spawn.ModelScale - 1.25f) < 0.0001f, "the summon's owner or scale was lost on the wire");
        return Task.CompletedTask;
    }
}
