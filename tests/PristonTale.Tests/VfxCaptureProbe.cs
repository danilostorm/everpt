using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Effects.Vfx;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.Effects;

namespace PristonTale.Tests;

// Plays named effects of the game data on a lit floor and saves frames (--capture-vfx <assets> <content> <out>
// [names, comma separated]); without names a representative set of the converted original scripts plays. Each effect
// must draw something at one of its sampled moments.
internal sealed class VfxCaptureProbe : Game, IVfxAnchors, IVfxServices
{
    private const float Step = 1f / 120f;

    private static readonly string[] DefaultEffects =
    [
        "script.SkillMetalionCompulsion1", "script.SkillMetalionMetalGolem", "script.SkillWarriorDestroyerHit", "script.SkillSagittarionForceofNature",
        "script.SkillSaintNightSwordOfustice", "script.SkillLancelotAssassinEye", "particle.HulkHit1", "particle.FireBigHit1", "particle.Skill3Hit1",
        "particle.Skill3FighterConcentration1", "particle.TwistJavelin2", "particle.Skill4Betserker", "particle.Skill3MechanicianSparkShield",
        "particle.DivineLightning", "particle.Skill2FighterRoar1"
    ];

    private static readonly float[] Times = [0.15f, 0.45f, 0.9f, 1.5f];

    private readonly string _assets;
    private readonly string _output;
    private readonly string[] _effects;
    private readonly List<string> _failures = [];
    private PristonRenderer _renderer = null!;
    private EffectLibrary _library = null!;
    private VfxSystem _system = null!;
    private VfxRenderer _vfx = null!;
    private PristonCamera _camera = null!;
    private RenderTarget2D _target = null!;
    private int _effect;
    private int _frame;
    private float _time;
    private int _drawn;

    public VfxCaptureProbe(string assets, string content, string output, IReadOnlyList<string>? effects = null)
    {
        _assets = assets;
        _output = output;
        _effects = effects?.ToArray() ?? DefaultEffects;
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
        _renderer = new PristonRenderer(GraphicsDevice, Content) { ShadowsEnabled = false, DayLengthSeconds = 0 };
        _renderer.SetTimeOfDay(12f);
        var stage = CaptureFloor.Create(_output, 600f, new Color(28, 28, 32));
        _renderer.AddStage(stage, Array.Empty<PristonModel>());
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward, new PristonCameraSettings { FieldOfViewDegrees = 50 });
        _system = new VfxSystem(this, this, _library.Vfx.Library);
        _vfx = new VfxRenderer(GraphicsDevice, Content);
        Begin(0);
    }

    private void Begin(int index)
    {
        _effect = index;
        _frame = 0;
        _time = 0f;
        _drawn = 0;
        _system.Clear();
        _renderer.TransientLights.Clear();
        var name = _effects[index];
        if (!_library.Vfx.Library.TryGet(name, out var effect))
        {
            _failures.Add($"{name} is not defined");
            return;
        }

        _system.Spawn(effect, new VfxSpawn(1, 2, new Vector3(0f, 0f, 0f), 0f));
    }

    protected override void Draw(GameTime gameTime)
    {
        var name = _effects[_effect];
        while (_time < Times[_frame])
        {
            _time += Step;
            _system.Update(Step);
            _renderer.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(Step)));
        }

        _camera.SetScriptedView(GraphicsDevice, new Vector3(0f, 90f, 150f), new Vector3(0f, 30f, 0f));
        GraphicsDevice.SetRenderTarget(_target);
        _renderer.ResetTemporalState();
        _renderer.Draw(_camera);
        _vfx.Draw(_system, _camera.View, _camera.Projection, _camera.Position, _renderer.SceneLinearDepth);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        _drawn = Math.Max(_drawn, _vfx.LastQuadCount + _vfx.LastModelCount);
        var file = Path.Combine(_output, string.Create(CultureInfo.InvariantCulture, $"{name}-{Times[_frame]:0.00}.png"));
        Save(file);

        // The exposed scene reads almost white, which hides additive light; the same frame on a dark backdrop shows it.
        GraphicsDevice.SetRenderTarget(_target);
        GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(18, 18, 22).ToVector4(), PristonDepth.Clear, 0);
        _vfx.DisplayOutput = true;
        _vfx.Draw(_system, _camera.View, _camera.Projection, _camera.Position, null);
        _vfx.DisplayOutput = false;
        GraphicsDevice.SetRenderTarget(null);
        Save(Path.ChangeExtension(file, ".dark.png"));

        Console.WriteLine($"{name} t={Times[_frame]:0.00}: {_vfx.LastQuadCount} quads, {_vfx.LastModelCount} models, {_system.ParticleCount} particles -> {file}");
        _frame++;
        if (_frame < Times.Length)
        {
            return;
        }

        if (_drawn == 0)
        {
            _failures.Add($"{name} drew nothing");
        }

        if (_effect + 1 < _effects.Length)
        {
            Begin(_effect + 1);
            return;
        }

        Assert.True(_failures.Count == 0, string.Join("; ", _failures));
        Exit();
    }

    private void Save(string file)
    {
        using var stream = File.Create(file);
        _target.SaveAsPng(stream, 960, 540);
    }

    public bool TryGetPosition(ulong entityId, out Vector3 position)
    {
        position = entityId == 2 ? new Vector3(60f, 0f, -60f) : Vector3.Zero;
        return entityId is 1 or 2;
    }

    public bool TryGetBone(ulong entityId, string bone, out Vector3 position)
    {
        position = new Vector3(0f, 40f, 0f);
        return entityId == 1;
    }

    public void AddLight(Vector3 position, Color color, int power, int decay) => _renderer.TransientLights.Add(position, color, power, decay);

    public void PlaySound(string path, Vector3 position, float volume)
    {
    }

    protected override void UnloadContent()
    {
        _vfx.Dispose();
        _renderer.Dispose();
        _library.Dispose();
        _target.Dispose();
    }
}
