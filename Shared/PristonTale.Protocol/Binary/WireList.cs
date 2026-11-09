namespace PristonTale.Protocol.Binary;

// A list entry of fixed size on the wire.
public interface IWireEntry<TSelf> where TSelf : IWireEntry<TSelf>
{
    static abstract int Size { get; }

    static abstract TSelf Read(ref PacketReader reader);

    void Write(ref PacketWriter writer);
}

// {int32 Count, Count entries}, the entries filling the rest of the message exactly.
public static class WireList
{
    public static void Write<T>(ref PacketWriter writer, IReadOnlyList<T> entries) where T : IWireEntry<T>
    {
        writer.Int32(entries.Count);
        for (var index = 0; index < entries.Count; index++)
        {
            entries[index].Write(ref writer);
        }
    }

    public static void Write<T>(ref PacketWriter writer, ReadOnlySpan<T> entries) where T : IWireEntry<T>
    {
        writer.Int32(entries.Length);
        foreach (var entry in entries)
        {
            entry.Write(ref writer);
        }
    }

    // A count outside 0..maxCount, or entries that do not fill the rest exactly, is invalid data named after the list.
    public static T[] Read<T>(ref PacketReader reader, int maxCount, string name) where T : IWireEntry<T>
    {
        var count = reader.Int32();
        if (count < 0 || count > maxCount || reader.Remaining != (long)count * T.Size)
        {
            throw new InvalidDataException($"{name} has an invalid count.");
        }

        var entries = new T[count];
        for (var index = 0; index < count; index++)
        {
            entries[index] = T.Read(ref reader);
        }

        return entries;
    }
}
