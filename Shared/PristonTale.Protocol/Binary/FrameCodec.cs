using System.Buffers.Binary;

namespace PristonTale.Protocol.Binary;

public enum FrameHeaderStatus
{
    NeedMoreData,
    Invalid,
    Complete
}

// Every message is one frame {int32 Size, uint32 Code, body}: Size counts the header and is at most MaxFrameSize. The
// frames travel inside the connection's TLS stream.
public static class FrameCodec
{
    public const int HeaderSize = 8;
    public const int MaxFrameSize = 8192;

    public static FrameHeaderStatus ReadHeader(ReadOnlySpan<byte> data, out int size, out uint code)
    {
        size = 0;
        code = 0;
        if (data.Length < HeaderSize)
        {
            return FrameHeaderStatus.NeedMoreData;
        }

        var candidateSize = BinaryPrimitives.ReadInt32LittleEndian(data);
        if (candidateSize < HeaderSize || candidateSize > MaxFrameSize)
        {
            return FrameHeaderStatus.Invalid;
        }

        size = candidateSize;
        code = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        return FrameHeaderStatus.Complete;
    }

    public static int WriteFrame(Span<byte> destination, uint code, ReadOnlySpan<byte> payload)
    {
        var size = checked(HeaderSize + payload.Length);
        if (size > MaxFrameSize || destination.Length < size)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        BinaryPrimitives.WriteInt32LittleEndian(destination, size);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], code);
        payload.CopyTo(destination[HeaderSize..]);
        return size;
    }
}
