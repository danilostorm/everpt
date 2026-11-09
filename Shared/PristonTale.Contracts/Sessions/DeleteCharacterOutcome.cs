namespace PristonTale.Contracts.Sessions;

public sealed record DeleteCharacterOutcome(bool Ok, string Message)
{
    public static DeleteCharacterOutcome Denied(string message) => new(false, message);

    public static DeleteCharacterOutcome Success() => new(true, "deleted");
}
