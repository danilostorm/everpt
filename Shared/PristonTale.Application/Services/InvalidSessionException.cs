namespace PristonTale.Application.Services;

public sealed class InvalidSessionException : Exception
{
    public InvalidSessionException()
        : base("invalid session")
    {
    }
}
