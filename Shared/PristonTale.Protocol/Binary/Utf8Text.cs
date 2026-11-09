using System.Text;

namespace PristonTale.Protocol.Binary;

// Text of the Etervale messages: {uint16 ByteLength, UTF-8 bytes}, cut at a whole character to fit its field. Malformed
// UTF-8 is refused (account names, chat and item texts come from clients).
public static class Utf8Text
{
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static void Write(ref PacketWriter writer, string? value, int maxBytes) => writer.Utf8(Fit(value ?? "", maxBytes));

    public static string Read(ref PacketReader reader, int maxBytes, string what)
    {
        var length = reader.UInt16();
        if (length > maxBytes)
        {
            throw new InvalidDataException($"{what} text is longer than its field allows.");
        }

        try
        {
            return Strict.GetString(reader.Bytes(length));
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException($"{what} text is not valid UTF-8.");
        }
    }

    public static int Size(string? value, int maxBytes) => 2 + Encoding.UTF8.GetByteCount(Fit(value ?? "", maxBytes));

    public static string Fit(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var used = 0;
        var end = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maxBytes)
            {
                break;
            }

            used += rune.Utf8SequenceLength;
            end += rune.Utf16SequenceLength;
        }

        return value[..end];
    }
}
