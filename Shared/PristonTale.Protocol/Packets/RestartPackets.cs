using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Every restart body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject version 0.
internal static class RestartWire
{
    public const ushort Version = 1;

    public static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    public static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Restart message");
}

// C->S: a dead player chooses where to stand up. {header, Guid OperationId, uint8 Choice (1 field, 2 town, 3 exit),
// uint8 0, uint16 0}.
public sealed record RestartRequestPacket(Guid OperationId, byte Choice) : IFramePacket
{
    public const int Size = 8 + 4 + 16 + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RestartRequest);
        RestartWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Choice);
        writer.Zero(3);
        return writer.Finish();
    }

    public static RestartRequestPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        RestartWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var choice = reader.Byte();
        return new RestartRequestPacket(operation, choice);
    }
}

// S->C: the answer to a restart. {header, Guid OperationId, uint8 Status, uint8 Choice, uint8 ScrollUsed, uint8 0,
// uint64 ExperienceLost, int64 GoldLost}.
public sealed record RestartResultPacket(Guid OperationId, byte Status, byte Choice, bool ScrollUsed, ulong ExperienceLost, long GoldLost)
{
    public const int Size = 8 + 4 + 16 + 4 + 8 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RestartResult);
        RestartWire.Header(ref writer);
        writer.Guid(OperationId);
        writer.Byte(Status);
        writer.Byte(Choice);
        writer.Byte(ScrollUsed ? (byte)1 : (byte)0);
        writer.Zero(1);
        writer.UInt64(ExperienceLost);
        writer.Int64(GoldLost);
        return writer.Finish();
    }

    public static RestartResultPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        RestartWire.ReadHeader(ref reader);
        var operation = reader.Guid();
        var status = reader.Byte();
        var choice = reader.Byte();
        var scroll = reader.Byte() != 0;
        reader.Skip(1);
        return new RestartResultPacket(operation, status, choice, scroll, reader.UInt64(), reader.Int64());
    }
}

// S->C: what standing up again would cost a dead player, whenever it changes. {header, uint8 Flags (1 the field restart
// is open, 2 a Resurrection Scroll waives the shares), uint8 0, uint16 0, uint64 ExperienceShare, int64 GoldShare}.
public sealed record RestartOfferPacket(bool FieldOpen, bool Scroll, ulong ExperienceShare, long GoldShare)
{
    public const int Size = 8 + 4 + 4 + 8 + 8;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.RestartOffer);
        RestartWire.Header(ref writer);
        writer.Byte((byte)((FieldOpen ? 1 : 0) | (Scroll ? 2 : 0)));
        writer.Zero(3);
        writer.UInt64(ExperienceShare);
        writer.Int64(GoldShare);
        return writer.Finish();
    }

    public static RestartOfferPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        RestartWire.ReadHeader(ref reader);
        var flags = reader.Byte();
        reader.Skip(3);
        return new RestartOfferPacket((flags & 1) != 0, (flags & 2) != 0, reader.UInt64(), reader.Int64());
    }
}
