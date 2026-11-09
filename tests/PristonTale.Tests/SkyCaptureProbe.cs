using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Tests;

internal sealed class SkyCaptureProbe : Game
{
    private readonly string _root;
    private readonly string _output;
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private int _profile;
    private RenderTarget2D _target = null!;

    public SkyCaptureProbe(string root, string content, string output)
    {
        _root = root;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _target = new RenderTarget2D(GraphicsDevice, 1280, 720, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _root);
        _renderer = new PristonRenderer(GraphicsDevice, Content)
        {
            Sky = _sky,
            ShadowsEnabled = false,
            DayLengthSeconds = 0f
        };
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(Color.Magenta);
        _renderer.SkyProfiles = new PristonSkyProfiles(_profile, _profile, _profile);
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0f, 200f, 0f), new Vector3(0f, 280f, -1000f));
        _renderer.Update(new GameTime(gameTime.TotalGameTime, TimeSpan.FromSeconds(3)));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new Color[width * height];
        _target.GetData(pixels);
        using var image = new Texture2D(GraphicsDevice, width, height);
        image.SetData(pixels);
        using var stream = File.Create(Path.Combine(_output, $"sky-{_profile:D2}.png"));
        image.SaveAsPng(stream, width, height);
        Console.WriteLine($"Sky {_profile}: {_renderer.LastFrameStats}");
        GraphicsDevice.SetRenderTarget(null);
        _profile++;
        if (_profile == _sky.ProfileCount)
        {
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _renderer.Dispose();
        _sky.Dispose();
        _target.Dispose();
    }
}
