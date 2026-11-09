using PristonTale.Client.Net.Presence;

namespace PristonTale.Gameplay.Screens.Framework;

// A way into the game's chat for what stands beside the game (the browser site's admin panel): the same typed lines a
// player could send, with the server deciding what they may do, and the lines that come back.
public static class GameConsole
{
    private const int KeptLines = 200;

    private static readonly Queue<string> Lines = new();
    private static Func<string, Task> _send;
    private static Func<int> _level;

    // The account's game master level while in the world, 0 otherwise.
    public static int GameMasterLevel => _level?.Invoke() ?? 0;

    public static bool InWorld => _send is not null;

    public static void Attach(Func<string, Task> send, Func<int> gameMasterLevel)
    {
        _send = send;
        _level = gameMasterLevel;
    }

    public static void Detach()
    {
        _send = null;
        _level = null;
    }

    public static bool TrySend(string line)
    {
        if (_send is null)
        {
            return false;
        }

        _ = _send(line);
        return true;
    }

    internal static void Report(ChatLineInfo line)
    {
        lock (Lines)
        {
            Lines.Enqueue($"{(int)line.Kind}\t{line.Text}");
            if (Lines.Count > KeptLines)
            {
                Lines.Dequeue();
            }
        }
    }

    public static string[] TakeLines()
    {
        lock (Lines)
        {
            var taken = Lines.ToArray();
            Lines.Clear();
            return taken;
        }
    }
}
