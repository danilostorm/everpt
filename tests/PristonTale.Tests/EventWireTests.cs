using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Skills;
using PristonTale.Contracts.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;

namespace PristonTale.Tests;

// No handles: every entity goes by its id.
internal sealed class NoEventHandles : IEventHandleWriter, IEventHandleReader
{
    public static readonly NoEventHandles Instance = new();

    public bool TryGetHandle(ulong entityId, out uint handle)
    {
        handle = 0;
        return false;
    }

    public bool TryGetEntity(uint handle, out ulong entityId)
    {
        entityId = 0;
        return false;
    }
}

// The compact combat, skill and status event lists: entities by the receiver's presence handles or by id, only the
// values an event has, one frame per message, and the encoder and decoder agreeing on the handles.
internal static class EventWireTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("event lists name a known entity by its handle and another by its id, carry only what an event has and split across frames", ListsAsync);
        yield return ("the encoder names the entities a receiver knows by the handles its decoder bound, and others by id", RoundTripAsync);
        yield return ("an event names an entity by a handle freed by another and taken again, and by the handles of a new field", ReuseAsync);
    }

    private sealed class Handles(params (ulong Id, uint Handle)[] pairs) : IEventHandleWriter, IEventHandleReader
    {
        public bool TryGetHandle(ulong entityId, out uint handle)
        {
            var found = pairs.FirstOrDefault(pair => pair.Id == entityId);
            handle = found.Handle;
            return found.Id != 0;
        }

        public bool TryGetEntity(uint handle, out ulong entityId)
        {
            entityId = pairs.FirstOrDefault(pair => pair.Handle == handle && pair.Id != 0).Id;
            return entityId != 0;
        }
    }

    private static Task ListsAsync()
    {
        var handles = new Handles((5, 0), (6, 300));
        var buffer = new byte[FrameCodec.MaxFrameSize];
        var hit = new CombatEventEntry(5, 0x1_0000_0001UL, CombatEventKind.Hit, CombatEventFlags.Critical, 0f, 0f, 1234, 0, 0, 0, 0, 0, 0);
        var swing = new CombatEventEntry(6, 5, CombatEventKind.SwingBegin, 0, 833.5f, 412.0625f, 0, 0, 77, 2560, -32, 99_968, 2048, 3);
        CombatEventEntry[] combat = [hit, swing];
        var length = CombatEventList.Write(buffer, combat, handles, out var written);
        Assert.True(written == 2 && CombatEventList.Read(buffer.AsSpan(0, length), handles).SequenceEqual(combat), "combat events did not round trip");
        length = CombatEventList.Write(buffer, [hit], handles, out _);
        Assert.True(length <= EventWire.HeaderSize + 12, $"a hit by a known attacker took {length} bytes");
        Assert.Throws<InvalidDataException>(() => CombatEventList.Read(buffer.AsSpan(0, length).ToArray(), NoEventHandles.Instance),
            "a handle the receiver does not hold was read");

        var start = new SkillEventEntry(6, 5, 0x08010004, SkillEventPhase.Start, 4, SkillEventFlags.Area, 714f, 414f, 320, 0, -640, 1024, 0, 76);
        var cancel = new SkillEventEntry(0x2_0000_0000UL, 0, 0x08010004, SkillEventPhase.Cancel, 1, 0, 0f, 0f, 0, 0, 0, 0, 0, 0);
        length = SkillEventList.Write(buffer, [start, cancel], handles, out _);
        Assert.True(SkillEventList.Read(buffer.AsSpan(0, length), handles).SequenceEqual([start, cancel]), "skill events did not round trip");
        var status = new StatusEffectEntry(5, 6, 0x06010001, StatusEffectOp.Tick, 2, 1, 0, StatusEffectFlags.Harmful, 10_000, 4_000, -35);
        length = StatusEffectList.Write(buffer, [status], handles, out _);
        Assert.True(StatusEffectList.Read(buffer.AsSpan(0, length), handles).SequenceEqual([status]), "a status did not round trip");

        var many = Enumerable.Range(0, 2000).Select(index => swing with { Damage = index }).ToArray();
        var read = new List<CombatEventEntry>();
        var messages = 0;
        for (var rest = many.AsSpan(); !rest.IsEmpty; messages++)
        {
            length = CombatEventList.Write(buffer, rest, handles, out written);
            Assert.True(written > 0 && length <= FrameCodec.MaxFrameSize, "a message held nothing or outgrew a frame");
            read.AddRange(CombatEventList.Read(buffer.AsSpan(0, length), handles));
            rest = rest[written..];
        }

        Assert.True(messages > 1 && read.SequenceEqual(many), $"{messages} messages did not carry every event in order");
        buffer[8] = 9;
        Assert.Throws<InvalidDataException>(() => CombatEventList.Read(buffer.AsSpan(0, length).ToArray(), handles), "another version was read");
        return Task.CompletedTask;
    }

    private static EntityView At(ulong id, float x, EntityKind kind = EntityKind.Monster) =>
        new(id, kind, false, new Vector3(x, 0, 0), 0f, MotionState.Stand, 1, 100, 100, default);

    private static Task ReuseAsync()
    {
        var self = At(1, 0, EntityKind.Player);
        var wire = new WireRoundTrip(1);
        wire.PushMerged(new WorldSnapshot(1, [self, At(10, 20)], fieldId: 3));
        var freed = wire.LastEntries.Single(entry => entry.EntityId == 10).Handle;
        wire.PushMerged(new WorldSnapshot(2, [self], fieldId: 3));
        var update = wire.PushMerged(new WorldSnapshot(3, [self, At(20, 30)], [new(20, 1, CombatEventKind.Hit, damage: 5)], 3));
        Assert.True(wire.LastEntries.Single(entry => entry.EntityId == 20).Handle == freed && update.Events.Single().AttackerId == 20,
            "the entity that took a freed handle was not named by it");

        update = wire.PushMerged(new WorldSnapshot(4, [self, At(30, 10)], [new(30, 1, CombatEventKind.Hit, damage: 6), new(1, 20, CombatEventKind.Hit, damage: 7)], 4));
        Assert.True(update.Events.Select(item => (item.AttackerId, item.TargetId)).SequenceEqual([(30UL, 1UL), (1UL, 20UL)]),
            "the events of a new field named the wrong entities");
        return Task.CompletedTask;
    }

    private static Task RoundTripAsync()
    {
        EntityView[] views = [At(1, 0, EntityKind.Player), At(10, 20), At(0x7_0000_0000UL, 40)];
        CombatEventView[] combat =
        [
            new(10, 1, CombatEventKind.Hit, damage: 15),
            new(1, 0x9_0000_0000UL, CombatEventKind.Hit, damage: 30),
            new(0x7_0000_0000UL, 10, CombatEventKind.Heal, damage: 45)
        ];
        StatusEventView[] statuses = [new(0x7_0000_0000UL, 1, 7, StatusEffectOp.Add, 1, 0, 0, false, false, 1000, 1000, 0)];
        var wire = new WireRoundTrip(1);
        var update = wire.PushMerged(new WorldSnapshot(5, views, combat, 3) { StatusEvents = statuses });
        Assert.True(update.Events.Select(item => (item.AttackerId, item.TargetId, item.Damage))
                .SequenceEqual([(10UL, 1UL, 15), (1UL, 0x9_0000_0000UL, 30), (0x7_0000_0000UL, 10UL, 45)]),
            $"events named the wrong entities: {string.Join(", ", update.Events.Select(item => $"{item.AttackerId}->{item.TargetId}"))}");
        Assert.True(update.StatusEffects.Single() is { EntityId: 0x7_0000_0000UL, SourceId: 1UL }, "a status named the wrong entities");
        return Task.CompletedTask;
    }
}
