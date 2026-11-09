using System.Runtime.Versioning;
using MonoGame.Interop;

namespace Microsoft.Xna.Framework;

/// <summary>The browser build's start: see monogame/web/README.md.</summary>
[SupportedOSPlatform("browser")]
public static class WebRuntime
{
    /// <summary>
    /// Connects to the page's graphics runtime. Call on .NET's main thread, the one that can call JavaScript, before the
    /// game starts on a thread of its own: that main thread must stay free, since browser networking completes on it.
    /// </summary>
    public static void Start()
    {
        WebStream.EnsureStarted();
        MGA.Start();
    }
}
