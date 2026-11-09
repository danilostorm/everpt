namespace PristonTale.Persistence.Abstractions.Accounts;

// GmLevel is the account's game-master level: 0 for players, 1 to 4 for game masters (see GameMasterLevels).
public sealed record AccountRecord(
    long Id,
    string UserId,
    string PasswordHash,
    bool IsBlocked,
    bool IsTester,
    int GmLevel = 0);
