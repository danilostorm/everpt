using PristonTale.Contracts.World;
using PristonTale.Protocol.Binary;

namespace PristonTale.Protocol.Packets;

// S->C {header, uint16 Version = 1, uint16 0, uint8 Kind, uint8 Intensity, uint8 WindHeading, uint8 Wind}: the weather
// of the receiver's field, sent when it enters the world and whenever it changes (a field of another sky included).
public readonly record struct WeatherPacket(WeatherState Weather) : IFramePacket
{
    public const ushort CurrentVersion = 1;
    public const int Size = 8 + WireHeader.Size + 4;

    public int Write(Span<byte> destination)
    {
        var writer = new PacketWriter(destination, EtervaleOpcodes.Weather);
        WireHeader.Write(ref writer, CurrentVersion);
        writer.Byte((byte)Weather.Kind);
        writer.Byte(Weather.Intensity);
        writer.Byte(Weather.WindHeading);
        writer.Byte(Weather.Wind);
        return writer.Finish();
    }

    public static WeatherPacket Read(ReadOnlySpan<byte> packet)
    {
        var reader = new PacketReader(packet, Size);
        WireHeader.Read(ref reader, "Weather");
        var kind = reader.Byte();
        if (kind > (byte)WeatherKind.Storm)
        {
            throw new InvalidDataException($"Weather has the unknown kind {kind}.");
        }

        return new WeatherPacket(new WeatherState((WeatherKind)kind, reader.Byte(), reader.Byte(), reader.Byte()));
    }
}
