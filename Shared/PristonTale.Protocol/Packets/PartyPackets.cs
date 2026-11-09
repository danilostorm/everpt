using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Party messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject version 0. Names are UTF-8
// with a uint16 byte length, within the character names' limit. Requests are not answered: PartyMembers and the chat
// lines show what changed.
public static class PartyWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Party message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Party message");
}

// C->S: ask a player to group. {header, uint64 TargetId}.
public sealed record PartyInvitePacket(ulong TargetId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyInvite);
        PartyWire.Header(ref writer);
        writer.UInt64(TargetId);
        return writer.Finish();
    }

    public static PartyInvitePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PartyWire.ReadHeader(ref reader);
        return new PartyInvitePacket(reader.UInt64());
    }
}

// C->S: answer the request PartyRequest showed. {header, uint64 FromId, uint8 Accept (0 or 1)}.
public sealed record PartyAnswerPacket(ulong FromId, bool Accept) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8 + 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyAnswer);
        PartyWire.Header(ref writer);
        writer.UInt64(FromId);
        writer.Byte(Accept ? (byte)1 : (byte)0);
        return writer.Finish();
    }

    public static PartyAnswerPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PartyWire.ReadHeader(ref reader);
        var from = reader.UInt64();
        return reader.Byte() switch
        {
            0 => new PartyAnswerPacket(from, false),
            1 => new PartyAnswerPacket(from, true),
            _ => throw new InvalidDataException("Party answer is neither yes nor no.")
        };
    }
}

// C->S: a party window button. {header, uint8 Command (PartyCommand), uint64 MemberId (0 for disband and leave)}.
public sealed record PartyCommandPacket(PartyCommand Command, ulong MemberId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 1 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyCommand);
        PartyWire.Header(ref writer);
        writer.Byte((byte)Command);
        writer.UInt64(MemberId);
        return writer.Finish();
    }

    public static PartyCommandPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        PartyWire.ReadHeader(ref reader);
        var command = reader.Byte();
        if (!Enum.IsDefined((PartyCommand)command))
        {
            throw new InvalidDataException("Party command is unknown.");
        }

        return new PartyCommandPacket((PartyCommand)command, reader.UInt64());
    }
}

// S->C: a player asks the receiver to group (the receiver's answer echoes FromId); a member asking its leader names the
// player it recommends. {header, uint64 FromId, text FromName, text Recommended (empty for a request to join)}.
public sealed record PartyRequestPacket(ulong FromId, string FromName, string Recommended) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 2 + 2;
    public const int MaxSize = MinimumSize + 2 * AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyRequest);
        PartyWire.Header(ref writer);
        writer.UInt64(FromId);
        Utf8Text.Write(ref writer, FromName, AccountWire.MaxNameBytes);
        Utf8Text.Write(ref writer, Recommended, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static PartyRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PartyWire.ReadHeader(ref reader);
        return new PartyRequestPacket(reader.UInt64(), PartyWire.Name(ref reader), PartyWire.Name(ref reader));
    }
}

// A party member: {uint64 EntityId, text Name, int32 Level, int32 Life, int32 MaxLife, float X, float Z} (its planar place in the
// world, which every field shares).
public readonly record struct PartyMemberWire(ulong EntityId, string Name, int Level, int Life, int MaxLife, float X, float Z);

// S->C: the receiver's party, the leader first; no member when it has none. {header, uint8 Count, members}.
public sealed record PartyMembersPacket(IReadOnlyList<PartyMemberWire> Members) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 1;
    private const int MaxMemberSize = 8 + 2 + AccountWire.MaxNameBytes + 4 * 3 + 4 * 2;
    public const int MaxSize = MinimumSize + PartyLimits.MaxMembers * MaxMemberSize;

    public int Write(Span<byte> destination)
    {
        if (Members.Count > PartyLimits.MaxMembers)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many party members.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.PartyMembers);
        PartyWire.Header(ref writer);
        writer.Byte((byte)Members.Count);
        foreach (var member in Members)
        {
            writer.UInt64(member.EntityId);
            Utf8Text.Write(ref writer, member.Name, AccountWire.MaxNameBytes);
            writer.Int32(member.Level);
            writer.Int32(member.Life);
            writer.Int32(member.MaxLife);
            writer.Single(member.X);
            writer.Single(member.Z);
        }

        return writer.Finish();
    }

    public static PartyMembersPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        PartyWire.ReadHeader(ref reader);
        int count = reader.Byte();
        if (count > PartyLimits.MaxMembers)
        {
            throw new InvalidDataException("Party members has an invalid count.");
        }

        var members = new PartyMemberWire[count];
        for (var index = 0; index < count; index++)
        {
            members[index] = new PartyMemberWire(reader.UInt64(), PartyWire.Name(ref reader), reader.Int32(), reader.Int32(), reader.Int32(),
                reader.Single(), reader.Single());
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Party members has trailing bytes.");
        }

        return new PartyMembersPacket(members);
    }
}
