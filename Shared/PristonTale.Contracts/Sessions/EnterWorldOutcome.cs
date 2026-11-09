namespace PristonTale.Contracts.Sessions;

public enum EnterWorldFailure
{
    None = 0,
    InvalidSession = 1,
    AlreadyEntering = 2,
    NotFound = 3,
    InvalidSavedClassOrField = 4,
    AlreadyActive = 5,
    UnsupportedSavedState = 6,
    SessionClosed = 7,
    Rejected = 8
}

public sealed record EnterWorldOutcome(
    bool Ok,
    int FieldId,
    ulong EntityId,
    float SpawnX,
    float SpawnZ,
    string Message)
{
    public EnterWorldFailure Failure { get; init; }

    public static EnterWorldOutcome Denied(EnterWorldFailure failure) =>
        new(false, 0, 0, 0f, 0f, MessageFor(failure)) { Failure = failure };

    public static EnterWorldOutcome Success(int fieldId, ulong entityId, float spawnX, float spawnZ, string message) =>
        new(true, fieldId, entityId, spawnX, spawnZ, message);

    public static string MessageFor(EnterWorldFailure failure) => failure switch
    {
        EnterWorldFailure.None => "",
        EnterWorldFailure.InvalidSession => "invalid session",
        EnterWorldFailure.AlreadyEntering => "session already entering or playing",
        EnterWorldFailure.NotFound => "character not found",
        EnterWorldFailure.InvalidSavedClassOrField => "invalid saved character class or field",
        EnterWorldFailure.AlreadyActive => "character already active",
        EnterWorldFailure.UnsupportedSavedState => "invalid or unsupported saved character state; saved data preserved",
        EnterWorldFailure.SessionClosed => "session closed during entry",
        _ => "world entry rejected"
    };
}
