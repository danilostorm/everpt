using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Tests;

internal static class ClientLogTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the client's console goes to client.log beside the executable with the time, keeping the previous run's log", LogFileAsync);
    }

    private static Task LogFileAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "etervale-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (ClientLogFile.Open(directory))
            {
                ConsoleClientLog.Instance.Error("first run", new InvalidOperationException("broken"));
            }

            using (ClientLogFile.Open(directory))
            {
                Console.WriteLine("second run");
            }

            var current = File.ReadAllLines(Path.Combine(directory, ClientLogFile.FileName));
            var previous = File.ReadAllText(Path.Combine(directory, ClientLogFile.PreviousFileName));
            Assert.True(current is [var line] && line.EndsWith(" second run") && TimeOnly.TryParse(line[..12], out _),
                $"client.log holds {string.Join(" | ", current)}");
            Assert.True(previous.Contains("[error] first run: System.InvalidOperationException: broken"),
                "the previous run's log lost the error with its exception");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }
}
