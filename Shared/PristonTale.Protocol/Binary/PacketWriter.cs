using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

// Sequential little-endian writer for the Etervale messages. Callers size the destination from the message layout.
public ref struct PacketWriter
{
    private readonly Span<byte> _buffer;
    private int _position;

    public PacketWriter(Span<byte> buffer, uint code)
    {
        _buffer = buffer;
        _buffer[..FrameCodec.HeaderSize].Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[4..], code);
        _position = FrameCodec.HeaderSize;
    }

    public readonly int Position => _position;

    public void Int32(int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void UInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void Int16(short value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(_buffer[_position..], value);
        _position += 2;
    }

    public void UInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer[_position..], value);
        _position += 2;
    }

    public void Byte(byte value)
    {
        _buffer[_position] = value;
        _position++;
    }

    public void Single(float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(_buffer[_position..], value);
        _position += 4;
    }

    public void Int64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    public void UInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer[_position..], value);
        _position += 8;
    }

    // Little-endian base-128 (Packets.VarInt): 1 byte below 128, at most 10 for 64 bits.
    public void VarUInt(ulong value) => _position += Packets.VarInt.Write(_buffer[_position..], value);

    public void VarInt(int value) => VarUInt(Packets.VarInt.ZigZag(value));

    public void Guid(Guid value)
    {
        value.TryWriteBytes(_buffer.Slice(_position, 16));
        _position += 16;
    }

    public void Bytes(scoped ReadOnlySpan<byte> value)
    {
        value.CopyTo(_buffer[_position..]);
        _position += value.Length;
    }

    // {uint16 ByteLength, UTF-8 bytes} encoded in place; the caller has fitted the text to its field (Utf8Text).
    public void Utf8(string text)
    {
        var count = System.Text.Encoding.UTF8.GetBytes(text, _buffer[(_position + 2)..]);
        UInt16((ushort)count);
        _position += count;
    }

    public void Zero(int count)
    {
        _buffer.Slice(_position, count).Clear();
        _position += count;
    }

    public int Finish()
    {
        BinaryPrimitives.WriteInt32LittleEndian(_buffer, _position);
        return _position;
    }
}
