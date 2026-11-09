using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

// Sequential little-endian reader over one complete, size-checked packet. Overruns throw InvalidDataException.
public ref struct PacketReader
{
    private readonly ReadOnlySpan<byte> _packet;
    private int _position;

    public PacketReader(ReadOnlySpan<byte> packet, int minimumSize)
    {
        if (packet.Length < minimumSize || packet.Length < FrameCodec.HeaderSize
            || BinaryPrimitives.ReadInt32LittleEndian(packet) != packet.Length)
        {
            throw new InvalidDataException($"Priston packet is shorter than {minimumSize} bytes or its size field disagrees.");
        }

        _packet = packet;
        Code = BinaryPrimitives.ReadUInt32LittleEndian(packet[4..]);
        _position = FrameCodec.HeaderSize;
    }

    public uint Code { get; }

    public readonly int Position => _position;

    public readonly int Remaining => _packet.Length - _position;

    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > _packet.Length - _position)
        {
            throw new InvalidDataException("Priston packet field exceeds the packet size.");
        }

        var slice = _packet.Slice(_position, count);
        _position += count;
        return slice;
    }

    public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Take(4));

    public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

    public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Take(2));

    public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));

    public byte Byte() => Take(1)[0];

    public float Single() => BinaryPrimitives.ReadSingleLittleEndian(Take(4));

    public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Take(8));

    public ulong UInt64() => BinaryPrimitives.ReadUInt64LittleEndian(Take(8));

    public ulong VarUInt() => Packets.VarInt.Read(_packet, ref _position);

    public int VarInt() => Packets.VarInt.UnZigZag(VarUInt());

    // A varuint that must fit a non-negative int (levels, speeds, indices).
    public int VarUInt31()
    {
        var value = VarUInt();
        return value <= int.MaxValue ? (int)value : throw new InvalidDataException("Variable-length value exceeds 31 bits.");
    }

    // A varuint that must fit 32 bits (counts, levels, codes).
    public uint VarUInt32()
    {
        var value = VarUInt();
        return value <= uint.MaxValue ? (uint)value : throw new InvalidDataException("Variable-length value exceeds 32 bits.");
    }

    public Guid Guid() => new(Take(16));

    public ReadOnlySpan<byte> Bytes(int count) => Take(count);

    public void Skip(int count) => Take(count);
}
