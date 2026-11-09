using PristonTale.Contracts.Social;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// Contact list messages (the friends page). Each body starts with {uint16 Version = 1, uint16 Flags = 0}; readers reject
// version 0. Names are UTF-8 with a uint16 byte length, within the character names' limit. Both requests are answered
// by a ContactList for each list; a refused change also by a chat line.
public static class ContactWire
{
    public const ushort Version = 1;

    internal static void Header(ref PacketWriter writer) => WireHeader.Write(ref writer, Version);

    internal static void ReadHeader(ref PacketReader reader) => WireHeader.Read(ref reader, "Contact message");

    internal static string Name(ref PacketReader reader) => Utf8Text.Read(ref reader, AccountWire.MaxNameBytes, "Contact message");
}

// C->S: put a name on a list or take it off. {header, uint8 List (ContactList), uint8 Add (0 or 1), text Name}.
public sealed record ContactChangePacket(ContactList List, bool Add, string Name) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 1 + 1 + 2;
    public const int MaxSize = MinimumSize + AccountWire.MaxNameBytes;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ContactChange);
        ContactWire.Header(ref writer);
        writer.Byte((byte)List);
        writer.Byte(Add ? (byte)1 : (byte)0);
        Utf8Text.Write(ref writer, Name, AccountWire.MaxNameBytes);
        return writer.Finish();
    }

    public static ContactChangePacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ContactWire.ReadHeader(ref reader);
        var list = reader.Byte();
        var add = reader.Byte();
        if (!Enum.IsDefined((ContactList)list) || add > 1)
        {
            throw new InvalidDataException("Contact change is malformed.");
        }

        return new ContactChangePacket((ContactList)list, add == 1, ContactWire.Name(ref reader));
    }
}

// C->S: send the lists again (who of the friends is online now). {header}.
public sealed record ContactRefreshPacket : IFramePacket
{
    public const int Size = 8 + WireHeader.Size;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.ContactRefresh);
        ContactWire.Header(ref writer);
        return writer.Finish();
    }

    public static ContactRefreshPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        ContactWire.ReadHeader(ref reader);
        return new ContactRefreshPacket();
    }
}

// A name on a list and, for a friend, whether it plays now.
public readonly record struct ContactEntryWire(string Name, bool Online);

// S->C: one list in the order added (each request is answered by both). {header, uint8 List (ContactList), uint8 Count,
// entries {text Name, uint8 Online (0 on the ignored list)}}.
public sealed record ContactListPacket(ContactList List, IReadOnlyList<ContactEntryWire> Entries) : IFramePacket
{
    private const int MinimumSize = 8 + WireHeader.Size + 1 + 1;
    public const int MaxSize = MinimumSize + ContactLimits.MaxEntries * (2 + AccountWire.MaxNameBytes + 1);

    public int Write(Span<byte> destination)
    {
        if (Entries.Count > ContactLimits.MaxEntries)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Too many contacts.");
        }

        var writer = new PacketWriter(destination, EtervaleOpcodes.ContactList);
        ContactWire.Header(ref writer);
        writer.Byte((byte)List);
        writer.Byte((byte)Entries.Count);
        foreach (var entry in Entries)
        {
            Utf8Text.Write(ref writer, entry.Name, AccountWire.MaxNameBytes);
            writer.Byte(entry.Online ? (byte)1 : (byte)0);
        }

        return writer.Finish();
    }

    public static ContactListPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, MinimumSize);
        ContactWire.ReadHeader(ref reader);
        var list = reader.Byte();
        int count = reader.Byte();
        if (!Enum.IsDefined((ContactList)list) || count > ContactLimits.MaxEntries)
        {
            throw new InvalidDataException("Contact list is malformed.");
        }

        var entries = new ContactEntryWire[count];
        for (var index = 0; index < count; index++)
        {
            entries[index] = new ContactEntryWire(ContactWire.Name(ref reader), reader.Byte() != 0);
        }

        if (reader.Remaining != 0)
        {
            throw new InvalidDataException("Contact list has trailing bytes.");
        }

        return new ContactListPacket((ContactList)list, entries);
    }
}
