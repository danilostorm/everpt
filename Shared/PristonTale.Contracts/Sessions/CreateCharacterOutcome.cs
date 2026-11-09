namespace PristonTale.Contracts.Sessions;

public enum CreateCharacterFailure
{
    None = 0,
    NameLength = 1,
    UnknownClass = 2,
    InvalidFace = 3,
    NameTaken = 4,
    NoFreeSlot = 5,
    Rejected = 6
}

public sealed record CreateCharacterOutcome(bool Ok, string Message, CharacterSummary? Character)
{
    public CreateCharacterFailure Failure { get; init; }

    public static CreateCharacterOutcome Failed(CreateCharacterFailure failure) => new(false, MessageFor(failure), null) { Failure = failure };

    public static CreateCharacterOutcome Success(CharacterSummary character) =>
        new(true, "created", character);

    public static string MessageFor(CreateCharacterFailure failure) => failure switch
    {
        CreateCharacterFailure.None => "created",
        CreateCharacterFailure.NameLength => "name must be 3–16 characters",
        CreateCharacterFailure.UnknownClass => "unknown class",
        CreateCharacterFailure.InvalidFace => "invalid face",
        CreateCharacterFailure.NameTaken => "that name is taken",
        CreateCharacterFailure.NoFreeSlot => "no free character slots",
        _ => "character creation rejected"
    };
}
