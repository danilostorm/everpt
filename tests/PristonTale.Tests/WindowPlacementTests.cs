using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal static class WindowPlacementTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("a window without a usable saved position opens centred with its title bar on screen", CenteredAsync);
        yield return ("the window's last position and maximized state survive close and reopen", PersistenceAsync);
    }

    private static Task CenteredAsync()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        Assert.True(WindowPlacement.Centered(new Point(1280, 720), screen) == new Point(320, 180), "a window smaller than the screen is not centred on it");
        Assert.True(WindowPlacement.Centered(new Point(1920, 1080), screen) == new Point(0, WindowPlacement.TitleBar),
            "a window as tall as the screen left its title bar off the top");
        Assert.True(WindowPlacement.TitleOnScreen(new Point(100, 50), new Point(1280, 720), screen), "a window on the screen was not restored");
        Assert.True(!WindowPlacement.TitleOnScreen(new Point(-2000, 50), new Point(1280, 720), screen)
            && !WindowPlacement.TitleOnScreen(new Point(100, 10), new Point(1280, 720), screen)
            && !WindowPlacement.TitleOnScreen(new Point(100, 1200), new Point(1280, 720), screen),
            "a window whose title bar is off the screen was restored there");
        Assert.True(WindowPlacement.OnScreen(new Point(320, 180), new Point(1280, 720), screen)
            && !WindowPlacement.OnScreen(new Point(800, 500), new Point(1280, 720), screen), "a window spilling off the screen counts as on it");
        return Task.CompletedTask;
    }

    private static async Task PersistenceAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "priston-placement-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "client-settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, """{ "version": 1, "windowWidth": 1920, "windowHeight": 1009 }""");
            var store = new ClientSettingsStore(path, NullClientLog.Instance);
            var older = store.Load();
            Assert.True(older.WindowX is null && older.WindowY is null && !older.WindowMaximized,
                "a settings document without a window position does not open centred");
            var placed = older with { WindowX = -1200, WindowY = 140, WindowMaximized = true };
            Assert.True(placed.HasSameDisplay(older), "moving or maximizing the window counts as a display change");
            Assert.True(store.QueueSave(placed), "the window placement was rejected by the persistence queue");
            await store.CompleteAsync();
            var reopened = new ClientSettingsStore(path, NullClientLog.Instance);
            Assert.True(reopened.Load() == placed, "the window position or maximized state was lost on close/reopen");
            await reopened.CompleteAsync();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
