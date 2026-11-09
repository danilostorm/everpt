using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using PristonTale;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Screens.Framework;
using PristonTale.Protocol.Transport;

[assembly: SupportedOSPlatform("browser")]

// The browser's entry point; main.js passes the address of the site's bridge to the game server. The site's panels reach the
// game through the exports here, which queue their work for the game thread (WebGame).
public static partial class Program
{
    // The game's blocking loop gets its own thread: .NET's main thread must stay free for the browser's networking.
    private const int GameThreadStackBytes = 16 * 1024 * 1024;

    internal static readonly ConcurrentQueue<string> PendingSettings = new();
    internal static readonly ConcurrentQueue<string> PendingCommands = new();

    public static void Main(string[] args)
    {
        WebSocketTransport.BridgeAddress = args[0];
        WebRuntime.Start();
        new Thread(RunGame, GameThreadStackBytes) { Name = "Game" }.Start();
    }

    private static void RunGame()
    {
        Console.WriteLine($"[Web] Game thread {Environment.CurrentManagedThreadId}.");
        // Not disposed: the page closes with the game, and a failed start must report its own exception, not one of cleanup.
        var game = new WebGame();
        try
        {
            game.Run();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.ToString());
            throw;
        }
    }

    [JSExport]
    public static Task ApplySettings(string json)
    {
        PendingSettings.Enqueue(json);
        return Task.CompletedTask;
    }

    [JSExport]
    public static Task SendCommand(string line)
    {
        PendingCommands.Enqueue(line);
        return Task.CompletedTask;
    }

    // What the admin panel shows: the level, whether the player is in the world, and the chat lines since the last call.
    [JSExport]
    public static Task<string> ReadConsole()
    {
        // Plain text, not JSON: reflection-based serialization never returned on this thread. Lines are "kind TAB text".
        var head = $"{GameConsole.GameMasterLevel}\t{(GameConsole.InWorld ? 1 : 0)}";
        return Task.FromResult(string.Join('\n', [head, .. GameConsole.TakeLines()]));
    }
}

[SupportedOSPlatform("browser")]
internal sealed class WebGame : Game1
{
    protected override void Update(GameTime gameTime)
    {
        while (Program.PendingSettings.TryDequeue(out var json))
        {
            try
            {
                var settings = ClientSettingsStore.Parse(json);
                var quality = Presentation.Requested.Quality;
                Presentation.Requested = settings.Quality != quality && settings.Quality != GraphicsQuality.Custom ? settings.WithQuality(settings.Quality) : settings;
            }
            catch (JsonException exception)
            {
                Console.Error.WriteLine($"[Settings] Not applied: {exception.Message}");
            }
        }

        // The desktop's confirmation guards against a display mode the monitor cannot show; a canvas has no such risk.
        if (Presentation.AwaitingConfirmation)
        {
            Presentation.ConfirmRequested = true;
        }

        while (Program.PendingCommands.TryDequeue(out var line))
        {
            GameConsole.TrySend(line);
        }

        base.Update(gameTime);
    }
}
