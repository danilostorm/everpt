using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

// The three potions (life, mana, stamina) side by side over a dark floor at night, at four times after the drink.
internal sealed class PotionCaptureProbe : Game
{
    private readonly string _assets;
    private readonly string _output;
    private EffectLibrary _library = null!;
    private PristonRenderer _renderer = null!;
    private VfxSystem _system = null!;
    private VfxRenderer _effects = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private readonly float[] _times = [0.15f, 0.45f, 0.8f, 1.2f];
    private int _sample;

    public PotionCaptureProbe(string assets, string content, string output)
    {
        _assets = assets;
        Content = new AbsoluteContentManager(Services, content);
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 960,
            PreferredBackBufferHeight = 540,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        _library = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        _library.Vfx.CompileAll();
        ItemVfx.Register(_library.Vfx.Library);
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(0);
        var material = new PristonMaterial(true, 1, 0, 1, true, Vector3.One, 0, 1, PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var floor = new Color(40, 40, 40);
        var stage = new PristonStage("potion-fixture.smd", [material],
            [new(new Vector3(-400, 0, -400), floor), new(new Vector3(-400, 0, 400), floor), new(new Vector3(400, 0, 400), floor), new(new Vector3(400, 0, -400), floor)],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [], new BoundingBox(new Vector3(-400, 0, -400), new Vector3(400, 0, 400)));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _system = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), _library.Vfx.Library);
        _effects = new VfxRenderer(GraphicsDevice, Content);
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
    }

    protected override void Draw(GameTime gameTime)
    {
        _system.Clear();
        for (var resource = 0; resource < 3; resource++)
        {
            Assert.True(_library.Vfx.Library.TryGet(ItemVfx.Potion(resource), out var potion), "potion effect is not registered");
            Assert.True(_system.Spawn(potion, new VfxSpawn(0, 0, new Vector3((resource - 1) * 95f, 48f, 0), 0f)).Spawned, "potion did not spawn");
        }

        for (float elapsed = 0f; elapsed < _times[_sample];)
        {
            var step = MathF.Min(1f / 140f, _times[_sample] - elapsed);
            _system.Update(step);
            _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
            elapsed += step;
        }

        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 95, 240), new Vector3(0, 35, 0));
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        _renderer.Draw(_camera);
        _effects.Draw(_system, _camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        const string name = "potions";
        var path = Path.Combine(_output, $"{name}-{_sample}.png");
        using (var stream = File.Create(path))
        {
            _target.SaveAsPng(stream, 960, 540);
        }

        Console.WriteLine($"{name}, t={_times[_sample]:0.00}s: {path}; parts={_system.ParticleCount}");
        _sample++;
        if (_sample == _times.Length)
        {
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _effects.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
