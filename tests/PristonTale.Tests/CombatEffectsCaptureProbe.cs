using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

internal sealed class CombatEffectsCaptureProbe : Game
{
    private static readonly float[] ReturnTimes = [.4f, 1.5f, 2.3f, 3.5f, 4f];
    private readonly string _assets;
    private readonly string _output;
    private readonly float[] _times = [0, .3f, .8f, 1.05f, 1.4f, 2.2f];
    private PristonRenderer _renderer = null!;
    private EffectLibrary _library = null!;
    private VfxRenderer _effects = null!;
    private VfxSystem _effectSystem = null!;
    private MeteorEffects _meteors = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private int _sample;
    private Color[] _baseline = [];
    private bool _sawIllumination;

    public CombatEffectsCaptureProbe(string assets, string content, string output)
    {
        _assets = assets;
        _output = output;
        Content = new AbsoluteContentManager(Services, content);
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
        _target = new RenderTarget2D(GraphicsDevice, 960, 540, false, SurfaceFormat.Color, DepthFormat.Depth24);
        _library = EffectLibrary.LoadBootstrap(GraphicsDevice, Content, _assets, ConsoleClientLog.Instance);
        _library.Vfx.CompileAll();
        ItemVfx.Register(_library.Vfx.Library);
        _effects = new VfxRenderer(GraphicsDevice, Content);
        Assert.True(Content.Load<Effect>("Effects/StaticSurface").Parameters["LocalLightCount"] is not null,
            "capture requires the rebuilt production shader");
        // No temporal history: each sample is one frame, and the light frame's history would linger in the next.
        var presentation = new PresentationSettings();
        presentation.ApplyRendering(new ClientSettings() with { Quality = GraphicsQuality.Custom, AntiAliasing = AntiAliasingMode.Off });
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: presentation)
        {
            ShadowsEnabled = false,
            DayLengthSeconds = 0
        };
        _renderer.SetTimeOfDay(12);
        var material = new PristonMaterial(true, 1, 0, 1, true, Vector3.One, 0, 1,
            PristonMaterialSurfaceFlags.None, "", "", [], [], 0, 0, 0);
        var stage = new PristonStage("effect-fixture.smd", [material],
            [new(new Vector3(-400, 0, -400), new Color(80, 80, 80)), new(new Vector3(-400, 0, 400), new Color(80, 80, 80)),
             new(new Vector3(400, 0, 400), new Color(80, 80, 80)), new(new Vector3(400, 0, -400), new Color(80, 80, 80))],
            [new(0, 1, 2, 0, -1), new(0, 2, 3, 0, -1)], [], [], new BoundingBox(new Vector3(-400, 0, -400), new Vector3(400, 0, 400)));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _effectSystem = new VfxSystem(VfxNoAnchors.Instance, new TransientLightServices(_renderer.TransientLights), _library.Vfx.Library);
        var scripts = new SystemScriptEffects(_effectSystem, _library.Vfx.Library);
        _meteors = new MeteorEffects(scripts, _renderer.TransientLights, new PristonCollisionWorld(stage));
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward);
        _meteors.Clear();
        _effectSystem.Clear();
    }

    protected override void Draw(GameTime gameTime)
    {
        _meteors.Clear();
        _effectSystem.Clear();
        _renderer.TransientLights.Clear();
        bool returning = _sample >= 9;
        bool meteor = _sample >= 3 && !returning;
        float time = 0f;
        if (returning)
        {
            time = ReturnTimes[_sample - 9];
        }
        else if (meteor)
        {
            time = _times[_sample - 3];
        }
        _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 340, 700), new Vector3(0, 180, 0));
        if (returning)
        {
            _camera.SetScriptedView(GraphicsDevice, new Vector3(0, 110, 240), new Vector3(0, 45, 0));
            Assert.True(_library.Vfx.Library.TryGet(ItemVfx.Return, out var sparkle), "the return effect is not registered");
            var handle = _effectSystem.Spawn(sparkle, new VfxSpawn(0, 0, Vector3.Zero, 0f));
            for (float elapsed = 0f; elapsed < time;)
            {
                var step = MathF.Min(1f / 140f, time - elapsed);
                _effectSystem.Update(step);
                _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                elapsed += step;
            }

            Assert.True(handle.Alive == (time < 4f), "return effect lifetime changed");
        }
        else if (meteor)
        {
            Assert.True(_meteors.Spawn(Vector3.Zero), "meteor could not spawn");
            for (float elapsed = 0; elapsed < time;)
            {
                float step = MathF.Min(1f / 140, time - elapsed);
                _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(step)));
                _meteors.Update(step);
                _effectSystem.Update(step);
                elapsed += step;
            }

            if (time > 1)
            {
                Assert.True(_meteors.Count == 0, "meteor tunneled through the floor");
                Assert.True(_effectSystem.Instances.Any(instance => string.Equals(instance.Effect.Name, "particle.FireBigHit1", StringComparison.OrdinalIgnoreCase)), "meteor did not emit its original impact");
            }
        }
        else if (_sample == 1)
        {
            _renderer.TransientLights.Add(new Vector3(0, 22, 0), new Color(100, 255, 100), 400, 4);
        }
        else if (_sample == 2)
        {
            _renderer.TransientLights.Add(new Vector3(0, 22, 0), new Color(100, 255, 100), 400, 4);
            _renderer.TransientLights.Update(2);
        }

        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(new Color(18, 22, 25));
        _renderer.Draw(_camera);
        _effects.Draw(_effectSystem, _camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = new Color[960 * 540];
        _target.GetData(pixels);
        if (_sample == 0)
        {
            _baseline = pixels;
        }
        else if (_sample == 1)
        {
            _sawIllumination = Greener(pixels);
            Assert.True(_sawIllumination, "daylight static stage ignored the temporary light");
        }
        else if (_sample == 2)
        {
            // Not bit-equal: the light frame moved the automatic exposure.
            Assert.True(!Greener(pixels), "expired light left stale shader illumination");
        }

        var name = $"light-{_sample}";
        if (returning)
        {
            name = $"return-{time:0.00}";
        }
        else if (meteor)
        {
            name = $"meteor-{time:0.00}";
        }
        string path = Path.Combine(_output, name + ".png");
        using var stream = File.Create(path);
        _target.SaveAsPng(stream, 960, 540);
        Console.WriteLine(path);
        _sample++;
        if (_sample == 14)
        {
            Exit();
        }
    }

    private bool Greener(Color[] pixels) => pixels.Where((pixel, index) => pixel.G > _baseline[index].G + 3).Any();

    protected override void UnloadContent()
    {
        _meteors.Clear();
        _effects.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
