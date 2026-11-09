using System.Diagnostics;

namespace PristonTale.Gameplay.UI;

// The same native link works in the desktop and Web clients. WebPT installs
// an action that navigates to the same-origin registration page. The desktop
// opens that page in the user's regular browser.
public static class RegistrationNavigation
{
    public static Action BrowserRequest { get; set; }

    public static void Open()
    {
        if (BrowserRequest is { } request)
        {
            request();
            return;
        }

        if (OperatingSystem.IsBrowser())
            return;

        try
        {
            Process.Start(new ProcessStartInfo("https://ever.hoststorm.cloud/cadastro")
            {
                UseShellExecute = true
            });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"[Registration] Could not open sign-up page: {error.GetType().Name}");
        }
    }
}
