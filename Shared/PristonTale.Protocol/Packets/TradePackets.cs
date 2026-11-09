using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Trade messages. Each body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject version 0. Names are UTF-8
// with a uint16 byte length, within the character names' limit. Requests are not answered: TradeState (after
// TradeDetails, the partner items' values) and chat lines show what changed.
public static class TradeWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Trade message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Trade message");

    // A message that carries only its header and a player id.
    internal static int WriteId(Span<byte> destination, uint code, ulong id)
    {
        var writer = new PacketWriter(destination, code);
        Header(ref writer);
        writer.UInt64(id);
        return writer.Finish();
    }

    internal static ulong ReadId(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, 8 + WireHeader.Size + 8);
        ReadHeader(ref reader);
        return reader.UInt64();
    }
}

// A bag item on the trade grid: {Guid ItemId, int32 Cell}.
public readonly record struct TradeEntryWire(Guid ItemId, int Cell) : IWireEntry<TradeEntryWire>
{
    public const int Size = 16 + 4;

    static int IWireEntry<TradeEntryWire>.Size => Size;

    public void Write(ref PacketWriter writer)
    {
        writer.Guid(ItemId);
        writer.Int32(Cell);
    }

    public static TradeEntryWire Read(ref PacketReader reader) => new(reader.Guid(), reader.Int32());
}

// C->S: ask a player to trade. {header, uint64 TargetId}.
public sealed record TradeRequestPacket(ulong TargetId) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8;

    public int Write(Span<byte> destination) => TradeWire.WriteId(destination, EtervaleOpcodes.TradeRequest, TargetId);

    public static TradeRequestPacket Read(ReadOnlySpan<byte> packet) => new(TradeWire.ReadId(packet));
}

// C->S: answer the request TradeAsked showed. {header, uint64 FromId, uint8 Accept (0 or 1)}.
public sealed record TradeAnswerPacket(ulong FromId, bool Accept) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size + 8 + 1;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.TradeAnswer);
        TradeWire.Header(ref writer);
        writer.UInt64(FromId);
        writer.Byte(Accept ? (byte)1 : (byte)0);
        return writer.Finish();
    }

    public static TradeAnswerPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        TradeWire.ReadHeader(ref reader);
        var from = reader.UInt64();
        return reader.Byte() switch
        {
            0 => new TradeAnswerPacket(from, false),
            1 => new TradeAnswerPacket(from, true),
            _ => throw new InvalidDataException("Trade answer is neither yes nor no.")
        };
    }
}

// C->S: the side's whole offer. {header, int64 Money, int32 Count, entries}.
public sealed record TradeOfferPacket(long Money, IReadOnlyList<TradeEntryWire> Entries) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 4;
    public const int MaxSize = MinimumSize + TradeLimits.MaxItems * TradeEntryWire.Size;

    public int Write(Span<byte> destination)
    {
        if (Entries.Count > TradeLimits.MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many trade entries.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.TradeOffer);
        TradeWire.Header(ref writer);
        writer.Int64(Money);
        WireList.Write(ref writer, Entries);
        return writer.Finish();
    }

    public static TradeOfferPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        TradeWire.ReadHeader(ref reader);
        var money = reader.Int64();
        return new TradeOfferPacket(money, WireList.Read<TradeEntryWire>(ref reader, TradeLimits.MaxItems, "Trade offer"));
    }
}

// C->S: lock the side's offer (Lock) or close the trade (Cancel). {header}.
public sealed record TradeSignalPacket(uint Code) : IFramePacket
{
    public const int Size = 8 + WireHeader.Size;

    public int Write(Span<byte> destination)
    {
        if (Code is not (EtervaleOpcodes.TradeLock or EtervaleOpcodes.TradeCancel))
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Not a trade signal.");
        }

        var writer = new PacketWriter(destination, Code);
        TradeWire.Header(ref writer);
        return writer.Finish();
    }

    public static TradeSignalPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        TradeWire.ReadHeader(ref reader);
        return new TradeSignalPacket(reader.Code);
    }
}

// S->C: a player asks the receiver to trade (the answer echoes FromId). {header, uint64 FromId, text FromName}.
public sealed record TradeAskedPacket(ulong FromId, string FromName) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 2;
    public const int MaxSize = MinimumSize + AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.TradeAsked);
        TradeWire.Header(ref writer);
        writer.UInt64(FromId);
        Utf8Text.Write(ref writer, FromName, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static TradeAskedPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        TradeWire.ReadHeader(ref reader);
        return new TradeAskedPacket(reader.UInt64(), TradeWire.Name(ref reader));
    }
}

// S->C: the receiver's trade window; PartnerId 0 once it trades with nobody. {header, uint64 PartnerId, text PartnerName,
// uint8 Flags (1 own offer locked, 2 the partner's), int64 OwnMoney, int64 PartnerMoney, int32 Count, own entries, int32
// Count, the partner's items (StoreItemEntry, anchored at their trade grid cells)}.
public sealed record TradeStatePacket(ulong PartnerId, string PartnerName, byte Flags, long OwnMoney, long PartnerMoney, IReadOnlyList<TradeEntryWire> Own,
    IReadOnlyList<StoreItemEntry> PartnerItems) : IFramePacket
{
    public const byte OwnLocked = 1;
    public const byte PartnerLocked = 2;
    private const int MinimumSize = 8 + WireHeader.Size + 8 + 2 + 1 + 8 + 8 + 4 + 4;
    public const int MaxSize = MinimumSize + AccountWire.MaxNameBytes + TradeLimits.MaxItems * (TradeEntryWire.Size + StoreItemEntry.MaxSize);

    public int Write(Span<byte> destination)
    {
        if (Own.Count > TradeLimits.MaxItems || PartnerItems.Count > TradeLimits.MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many trade items.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.TradeState);
        TradeWire.Header(ref writer);
        writer.UInt64(PartnerId);
        Utf8Text.Write(ref writer, PartnerName, AccountWire.MaxNameBytes);
        writer.Byte(Flags);
        writer.Int64(OwnMoney);
        writer.Int64(PartnerMoney);
        writer.Int32(Own.Count);
        foreach (var entry in Own)
        {
            entry.Write(ref writer);
        }

        writer.Int32(PartnerItems.Count);
        foreach (var item in PartnerItems)
        {
            item.Write(ref writer);
        }

        return writer.Finish();
    }

    public static TradeStatePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        TradeWire.ReadHeader(ref reader);
        var partner = reader.UInt64();
        var name = TradeWire.Name(ref reader);
        var flags = reader.Byte();
        var ownMoney = reader.Int64();
        var partnerMoney = reader.Int64();
        var own = new TradeEntryWire[Count(ref reader)];
        for (var index = 0; index < own.Length; index++)
        {
            own[index] = TradeEntryWire.Read(ref reader);
        }

        var items = new StoreItemEntry[Count(ref reader)];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = StoreItemEntry.Read(ref reader);
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Trade state has trailing bytes.");
        }

        return new TradeStatePacket(partner, name, flags, ownMoney, partnerMoney, own, items);
    }

    private static int Count(ref PacketReader reader)
    {
        var count = reader.Int32();
        if (count < 0 || count > TradeLimits.MaxItems)
        {
            throw new InvalidDataException("Trade state has an invalid count.");
        }

        return count;
    }
}

// S->C: the values of the partner's offered items, in TradeState's order, sent just before it. {header, int32 Count,
// ItemDetailEntry entries}.
public sealed record TradeDetailsPacket(IReadOnlyList<ItemDetailEntry> Items) : IFramePacket
{
    private const int HeaderSize = 8 + WireHeader.Size + 4;
    public const int MaxSize = HeaderSize + TradeLimits.MaxItems * ItemDetailEntry.Size;

    public int Write(Span<byte> destination)
    {
        if (Items.Count > TradeLimits.MaxItems)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many trade details.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.TradeDetails);
        TradeWire.Header(ref writer);
        WireList.Write(ref writer, Items);
        return writer.Finish();
    }

    public static TradeDetailsPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, HeaderSize);
        TradeWire.ReadHeader(ref reader);
        return new TradeDetailsPacket(WireList.Read<ItemDetailEntry>(ref reader, TradeLimits.MaxItems, "Trade details"));
    }
}
