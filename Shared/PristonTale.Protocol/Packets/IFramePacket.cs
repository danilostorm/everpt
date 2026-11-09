namespace PristonTale.Protocol.Packets;

// A message that writes itself as one whole frame (header included) and returns its length. A sender takes it as a
// constrained generic, so a struct message is written without boxing.
public interface IFramePacket
{
    int Write(Span<byte> destination);
}
