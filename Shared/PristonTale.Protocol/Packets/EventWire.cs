using System.Buffers.Binary;
using PristonTale.Contracts.Units;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// The presence handles of one receiver as its encoder gave them (EventWire.WriteEntity).
public interface IEventHandleWriter
{
    bool TryGetHandle(ulong entityId, out uint handle);
}

// The presence handles of one receiver as its decoder bound them (EventWire.ReadEntity).
public interface IEventHandleReader
{
    bool TryGetEntity(uint handle, out ulong entityId);
}

// One entry of an event list: at least MinSize and at most MaxSize bytes on the wire.
public interface IEventEntry<T> where T : IEventEntry<T>
{
    static abstract int MinSize { get; }

    static abstract int MaxSize { get; }

    void Write(ref PacketWriter writer, IEventHandleWriter handles);

    static abstract T Read(ref PacketReader reader, IEventHandleReader handles);
}

// The compact form the combat, skill and status event lists share. A message is {header, uint16 Version, uint16 Count}
// and Count entries that fill it exactly; an entry names entities by the receiver's presence handles. The writer
// resolves a handle when it writes and the reader when it reads: both see the same spawns and removals before an event
// list, as every message of a connection is written and read in order.
//
// - Entity: varuint 0 none, 1 then varuint EntityId (an entity the receiver has no handle for), n >= 2 the handle n - 2.
// - Time: varuint in sixteenths of a millisecond, at most 2^32 - 1.
// - Point: the fixed-point X, Y, Z of the entry at the presence's 1/8 of a unit, three int24 (PresencePoint).
public static class EventWire
{
    public const int HeaderSize = 8 + 2 + 2;
    public const int MaxEntityBytes = 1 + VarInt.MaxBytes64;
    public const int MaxTimeBytes = VarInt.MaxBytes32;
    public const int PointBytes = 3 * PresenceWire.Int24Bytes;
    private const int MaxCount = ushort.MaxValue;
    private const int CountOffset = HeaderSize - 2;
    private const float TimeUnitsPerMs = 16f;
    private const int FixedPerPoint = GameUnits.FixedOne / (int)PresencePoint.PositionScale;

    // Writes the entries from the first while one more of the largest size fits the destination and a frame; written is
    // how many the message holds.
    public static int Write<T>(Span<byte> destination, uint code, ushort version, ReadOnlySpan<T> entries, IEventHandleWriter handles, out int written)
        where T : IEventEntry<T>
    {
        var writer = new PacketWriter(destination, code);
        writer.UInt16(version);
        writer.UInt16(0);
        var limit = Math.Min(destination.Length, FrameCodec.MaxFrameSize);
        written = 0;
        while (written < entries.Length && written < MaxCount && writer.Position + T.MaxSize <= limit)
        {
            entries[written++].Write(ref writer, handles);
        }

        if (written == 0 && !entries.IsEmpty)
        {
            throw new ArgumentException("The destination cannot hold one entry.", nameof(destination));
        }

        var length = writer.Finish();
        BinaryPrimitives.WriteUInt16LittleEndian(destination[CountOffset..], (ushort)written);
        return length;
    }

    public static T[] Read<T>(ReadOnlySpan<byte> packet, ushort version, string name, IEventHandleReader handles) where T : IEventEntry<T>
    {
        var reader = new PacketReader(packet, HeaderSize);
        if (reader.UInt16() != version)
        {
            throw new InvalidDataException($"{name} has an unsupported version.");
        }

        var count = reader.UInt16();
        if (count * T.MinSize > reader.Remaining)
        {
            throw new InvalidDataException($"{name} holds fewer bytes than its count needs.");
        }

        var entries = new T[count];
        for (var index = 0; index < entries.Length; index++)
        {
            entries[index] = T.Read(ref reader, handles);
        }

        return reader.Remaining == 0 ? entries : throw new InvalidDataException($"{name} does not end with its last entry.");
    }

    public static void WriteEntity(ref PacketWriter writer, ulong entityId, IEventHandleWriter handles)
    {
        if (entityId == 0)
        {
            writer.VarUInt(0);
        }
        else if (handles.TryGetHandle(entityId, out var handle))
        {
            writer.VarUInt((ulong)handle + 2);
        }
        else
        {
            writer.VarUInt(1);
            writer.VarUInt(entityId);
        }
    }

    public static ulong ReadEntity(ref PacketReader reader, IEventHandleReader handles)
    {
        var tag = reader.VarUInt();
        if (tag == 0)
        {
            return 0;
        }

        if (tag == 1)
        {
            var entityId = reader.VarUInt();
            return entityId != 0 ? entityId : throw new InvalidDataException("Event names entity 0 by id.");
        }

        if (tag - 2 > uint.MaxValue || !handles.TryGetEntity((uint)(tag - 2), out var bound))
        {
            throw new InvalidDataException("Event names a presence handle the receiver does not hold.");
        }

        return bound;
    }

    public static void WriteTime(ref PacketWriter writer, float ms) =>
        writer.VarUInt((uint)Math.Clamp(MathF.Round(ms * TimeUnitsPerMs), 0f, uint.MaxValue));

    public static float ReadTime(ref PacketReader reader) => reader.VarUInt32() / TimeUnitsPerMs;

    public static void WritePoint(ref PacketWriter writer, int x, int y, int z)
    {
        Span<byte> point = stackalloc byte[PointBytes];
        var offset = 0;
        PresenceWire.WriteInt24(point, ref offset, ToPoint(x));
        PresenceWire.WriteInt24(point, ref offset, ToPoint(y));
        PresenceWire.WriteInt24(point, ref offset, ToPoint(z));
        writer.Bytes(point);
    }

    public static (int X, int Y, int Z) ReadPoint(ref PacketReader reader)
    {
        var point = reader.Bytes(PointBytes);
        var offset = 0;
        return (PresenceWire.ReadInt24(point, ref offset) * FixedPerPoint, PresenceWire.ReadInt24(point, ref offset) * FixedPerPoint,
            PresenceWire.ReadInt24(point, ref offset) * FixedPerPoint);
    }

    // Rounded as PresencePoint rounds a position (half to even).
    private static int ToPoint(int fixedValue) =>
        (int)Math.Clamp(Math.Round(fixedValue / (double)FixedPerPoint), -PresencePoint.Limit, PresencePoint.Limit);
}
