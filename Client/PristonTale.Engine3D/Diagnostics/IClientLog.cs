namespace PristonTale.Engine3D.Diagnostics;

public interface IClientLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception exception = null);
}

public sealed class ConsoleClientLog : IClientLog
{
    public static readonly ConsoleClientLog Instance = new();

    public void Info(string message) => Console.WriteLine(message);
    public void Warn(string message) => Console.WriteLine("[warn] " + message);

    public void Error(string message, Exception exception = null) =>
        Console.WriteLine(exception is null ? "[error] " + message : $"[error] {message}: {exception}");
}

public sealed class NullClientLog : IClientLog
{
    public static readonly NullClientLog Instance = new();

    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception exception = null) { }
}
