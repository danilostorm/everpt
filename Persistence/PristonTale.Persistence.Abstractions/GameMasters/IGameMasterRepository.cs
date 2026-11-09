namespace PristonTale.Persistence.Abstractions.GameMasters;

// One game-master command as typed in chat, with the level it needed and what the server did with it.
public sealed record GmAuditEntry(
    long AccountId,
    string UserId,
    string CharacterName,
    int GmLevel,
    string Command,
    int RequiredLevel,
    bool Accepted,
    string Result);

public sealed record GmAuditRecord(
    long Id,
    long AccountId,
    string UserId,
    string CharacterName,
    int GmLevel,
    string Command,
    int RequiredLevel,
    bool Accepted,
    string Result,
    DateTime CreatedAt);

public enum CharacterTransfer
{
    Moved = 0,
    NoCharacter = 1,
    NoAccount = 2,
    NoFreeSlot = 3
}

public interface IGameMasterRepository
{
    // False when no account has this user id.
    Task<bool> SetGmLevelAsync(string userId, int level, CancellationToken ct = default);

    Task AppendAuditAsync(GmAuditEntry entry, CancellationToken ct = default);

    // Newest first.
    Task<IReadOnlyList<GmAuditRecord>> RecentAuditAsync(int limit, CancellationToken ct = default);

    // Moves a character to another account, into that account's first free slot below maxSlots.
    Task<CharacterTransfer> MoveCharacterAsync(string characterName, string targetUserId, int maxSlots, CancellationToken ct = default);
}
