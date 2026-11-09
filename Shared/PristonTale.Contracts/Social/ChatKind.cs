namespace PristonTale.Contracts.Social;

// The kind of a chat line: the server picks it, the wire carries it as a byte and the client colours and files the line
// by it. Append only.
public enum ChatKind : byte
{
    // Replies to the player's own commands and other server lines (orange-red).
    Server = 0,
    Whisper = 1,
    System = 2,
    Notice = 3,
    Party = 4,
    Clan = 5,
    Trade = 6,

    // The assistant window's lines (quest hints).
    Assist = 9,

    // A spoken line of a player, the local player's own included.
    Player = 0x80
}
