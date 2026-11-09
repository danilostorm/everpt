using System.Numerics;

namespace PristonTale.Application.Chat;

// The connection of one player in the world, as the chat and game-master commands reach it. Every member is called
// from any thread and must not block.
public interface IChatLink
{
    void Deliver(ChatLine line);

    // Moves the client to a position (forced warp) after the server already placed the player there.
    void Warp(int fieldId, Vector3 position);

    // Asks the client to save and quit (server shutdown).
    void RequestClose();

    void Disconnect(string reason);

    string RemoteAddress { get; }

    // Packets received and sent on this connection so far.
    long PacketsReceived { get; }
    long PacketsSent { get; }
}
