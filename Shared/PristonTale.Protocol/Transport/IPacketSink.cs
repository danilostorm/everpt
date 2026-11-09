namespace PristonTale.Protocol.Transport;

// Destination for complete frames; the connection's TLS stream protects every one.
public interface IPacketSink
{
    bool Send(ReadOnlySpan<byte> packet);
}
