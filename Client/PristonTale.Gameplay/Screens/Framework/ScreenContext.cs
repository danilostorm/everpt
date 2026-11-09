using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Gameplay.Audio;

namespace PristonTale.Gameplay.Screens.Framework;

public sealed class ScreenContext
{
    private readonly Action<GameScreen> _requestScreen;
    private readonly Action _renewBackend;

    public ScreenContext(
        GraphicsDevice graphicsDevice,
        ContentManager content,
        SpriteBatch spriteBatch,
        SpriteFont font,
        string assetRoot,
        IGameBackend backend,
        IClientLog log,
        Action<GameScreen> requestScreen,
        PristonTale.Engine3D.Rendering.PresentationSettings presentation = null,
        PristonTale.Gameplay.UI.Rendering.HudAtlas uiAtlas = null,
        PristonTale.Engine3D.Rendering.PristonSkyLibrary sky = null,
        PristonTale.Gameplay.Effects.EffectLibrary effects = null,
        PristonAudioLibrary audio = null,
        Action renewBackend = null)
    {
        GraphicsDevice = graphicsDevice;
        Content = content;
        SpriteBatch = spriteBatch;
        Font = font;
        AssetRoot = assetRoot;
        Backend = backend;
        Log = log ?? ConsoleClientLog.Instance;
        _requestScreen = requestScreen;
        Presentation = presentation ?? new PristonTale.Engine3D.Rendering.PresentationSettings();
        UiAtlas = uiAtlas;
        Sky = sky;
        Effects = effects;
        Audio = audio;
        _renewBackend = renewBackend;
    }

    public PristonTale.Engine3D.Rendering.PristonSkyLibrary Sky { get; }
    public PristonTale.Gameplay.Effects.EffectLibrary Effects { get; }
    public PristonAudioLibrary Audio { get; }
    public GraphicsDevice GraphicsDevice { get; }
    public ContentManager Content { get; }
    public SpriteBatch SpriteBatch { get; }
    public SpriteFont Font { get; }
    public string AssetRoot { get; }
    public bool IsWindowActive { get; internal set; } = true;
    public PristonTale.Engine3D.Rendering.PresentationSettings Presentation { get; }
    public PristonTale.Gameplay.UI.Rendering.HudAtlas UiAtlas { get; }

    public IClientLog Log { get; }

    public IGameBackend Backend { get; private set; }

    // Characters typed into the window (the operating system's text input, with its layout and repeat), drained by the
    // chat input each frame.
    public TypedText TypedText { get; } = new();

    // Set when the server asked the client to save and quit; the game closes like the window's close button.
    public bool ExitRequested { get; set; }

    public void RequestScreen(GameScreen next) => _requestScreen(next);

    // Replaces a backend that already logged in or lost its connection; the old one is disposed off the game thread.
    public void RenewBackend() => _renewBackend?.Invoke();

    internal void ReplaceBackend(IGameBackend backend) => Backend = backend;
}
