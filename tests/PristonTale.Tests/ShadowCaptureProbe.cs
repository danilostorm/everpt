using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Client.Net.Presence;
using PristonTale.Client.Net.WorldCatalog;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Gameplay.World.Remote;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.World;
using PristonTale.Simulation.World.Npcs;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Assets;
using PristonTale.Contracts.Characters;
using PristonTale.Simulation.World.Monsters;

namespace PristonTale.Tests;

// Characters and a large monster in Navisko in the sun and beside a lamp at night, close to the camera, with the screen
// space shadowing on and off and its occlusion view, a turning camera, and Navisko's NPCs where the town places them:
// shadows and surfaces on the ground and on the bodies over consecutive frames.
internal sealed class ShadowCaptureProbe : Game
{
    private sealed record Shot(string Name, float Hour, int Frames, bool AtLamp, bool Close, Func<ClientSettings, ClientSettings> Settings,
        bool SunMoves = false, int Npc = -1, bool Orbit = false, bool Measure = false);

    private static readonly Shot[] Shots =
    [
        new("day", 14f, 3, false, false, static s => s),
        new("day-close", 14f, 3, false, true, static s => s),
        new("day-close-nocontact", 14f, 1, false, true, static s => s with { ContactShadows = false }),
        new("day-close-occlusion", 14f, 1, false, true, static s => s with { DebugView = PristonDebugView.AmbientOcclusion }),
        new("day-close-noshadow", 14f, 1, false, true, static s => s with { ShadowsEnabled = false }),
        new("night", 21.5f, 3, true, false, static s => s),
        new("morning-moving", 8f, 3, false, false, static s => s, SunMoves: true),
        new("orbit", 15f, 40, false, false, static s => s, Orbit: true),
        new("brightness", 10f, 600, false, false, static s => s, SunMoves: true, Measure: true),
        new("karina-afternoon", 16f, 20, false, true, static s => s, Npc: 0),
        new("karina-wide", 16f, 20, false, false, static s => s, Npc: 0),
        new("ariel", 16f, 20, false, true, static s => s, Npc: 1)
    ];

    // Hours the server's clock advances in one real second.
    private const float HoursPerSecond = 1000f / WorldClock.MinuteMs / 60f;

    // The game's usual resolution: at a quarter of it a shadow texel covers too few pixels to show its steps.
    private const int Width = 1920;
    private const int Height = 1080;

    private const ulong Priestess = 1;
    private const ulong Atalanta = 2;
    private const ulong Babel = 3;

    // Karina, Ariel, Casa and Sylpi where Navisko places them: the server's coordinates over 256 and its heading in
    // 4096ths of a turn, both mirrored in Z as the client receives them.
    private static readonly (string Model, float X, float Z, float Facing)[] NaviskoNpcs =
    [
        ("char/npc/SN-001/sn-001.inx", 5591526f / 256f, -262266f / 256f, ClientFacing(2014)),
        ("char/npc/SN-002/sn-002.inx", 5537856f / 256f, -297657f / 256f, ClientFacing(5772)),
        ("char/npc/SN-003/sn-003.inx", 5501933f / 256f, -254241f / 256f, ClientFacing(5363)),
        ("char/npc/SN-005/sn-005.inx", 5646992f / 256f, -298582f / 256f, ClientFacing(2517))
    ];

    // A shot that takes longer than this is stuck (an actor that never loaded, a device that stopped presenting).
    private static readonly TimeSpan ShotTimeout = TimeSpan.FromMinutes(2);

    private readonly string _assetRoot;
    private readonly string _output;
    private readonly PresentationSettings _presentation = new();
    private readonly System.Diagnostics.Stopwatch _shotTime = System.Diagnostics.Stopwatch.StartNew();
    private PristonRenderer _renderer = null!;
    private PristonSkyLibrary _sky = null!;
    private PristonCamera _camera = null!;
    private RemoteEntityManager _remote = null!;
    private PristonCollisionWorld _collision = null!;
    private Vector3 _sunSpot;
    private Vector3 _lampSpot;
    private Vector3 _focus;
    private int _shot = -1;
    private int _frame;
    private double _seconds;
    private double _shotSeconds;
    private Color[]? _previous;
    private Color[] _pixels = new Color[Width * Height];

    public ShadowCaptureProbe(string assetRoot, string contentRoot, string output)
    {
        _assetRoot = assetRoot;
        _output = output;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Width,
            PreferredBackBufferHeight = Height,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = false;
    }

    protected override void LoadContent()
    {
        Directory.CreateDirectory(_output);
        var field = new WorldCatalog().AllFields.Single(candidate => candidate.Id == "village-1");
        _sky = PristonSkyLibrary.LoadBootstrap(GraphicsDevice, _assetRoot);
        _renderer = new PristonRenderer(GraphicsDevice, Content, presentation: _presentation) { Sky = _sky, DayLengthSeconds = 0 };
        _renderer.SkyProfiles = new PristonSkyProfiles(PristonSkyProfiles.Resolve(field.Backdrop.Day),
            PristonSkyProfiles.Resolve(field.Backdrop.Glow), PristonSkyProfiles.Resolve(field.Backdrop.Night));
        var reader = new PristonSmdReader();
        var stage = reader.ReadStage(Resolve(field.StageSmd));
        var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
        _renderer.PrepareStageTexturesAsync(stage, models).GetAwaiter().GetResult();
        _renderer.AddStage(stage, models);
        _collision = new PristonCollisionWorld(stage);
        var start = field.StartPoints[0];
        _sunSpot = Floor(_collision, new Vector3(start.X, 5000f, -start.Z));
        var lamp = stage.Lights.Where(static light => light.Range > 50f)
            .MinBy(light => Vector2.DistanceSquared(new Vector2(light.Position.X, light.Position.Z), new Vector2(_sunSpot.X, _sunSpot.Z)));
        _lampSpot = lamp is null ? _sunSpot : Floor(_collision, new Vector3(lamp.Position.X + 60f, lamp.Position.Y, lamp.Position.Z + 40f));

        var loader = new PristonActorLoader();
        var actorScene = new RendererActorScene(_renderer);
        var catalog = new WorldRenderCatalog(new Dictionary<uint, MonsterRenderInfo>
        {
            [1] = new(1, "char/monster/Bguardian/Bguardian.inx", 1.2f, "Babel", 104, Simulation.World.Monsters.MonsterBrood.Normal, 8, 25)
        }, NaviskoNpcs.Select((npc, index) => new NpcRenderInfo((uint)(index + 1), npc.Model, 1f, npc.Model, NpcServiceSet.None))
            .ToDictionary(static info => info.TemplateId));
        _remote = new RemoteEntityManager(actorScene, new PlayerActorComposer(actorScene, loader), new ModelActorComposer(actorScene, loader),
            new CharacterClassRegistry(), _assetRoot, catalog, ConsoleClientLog.Instance);
        Place(_sunSpot);
    }

    private void Place(Vector3 spot)
    {
        _focus = spot;
        _remote.Apply(new PresenceUpdate(1,
        [
            new(Priestess, EntityKind.Player, false, Numeric(spot), 0.4f, MotionState.Stand, 64, 64,
                new PlayerAppearanceInfo("Priestess", PlayerClass.Priestess, 3, 0, 0, "", "", 90), 0),
            new(Atalanta, EntityKind.Player, false, Numeric(Floor(_collision, spot + new Vector3(-50f, 500f, -20f))), 2.2f, MotionState.Stand, 64,
                64, new PlayerAppearanceInfo("Atalanta", PlayerClass.Atalanta, 3, 1, 1, "", "", 90), 0),
            new(Babel, EntityKind.Monster, false, Numeric(Floor(_collision, spot + new Vector3(70f, 500f, -60f))), 3.6f, MotionState.Stand,
                100, 100, null, 1, Name: "Babel", Level: 104),
            .. NaviskoNpcs.Select((npc, index) => new EntitySpawnInfo((ulong)(10 + index), EntityKind.Npc, false,
                Numeric(NpcSpot(index)), npc.Facing, MotionState.Stand, 1, 1, null,
                (uint)(index + 1)))
        ], [], [], []));
    }

    protected override void Update(GameTime gameTime)
    {
        if (_shotTime.Elapsed > ShotTimeout)
        {
            var name = _shot >= 0 && _shot < Shots.Length ? Shots[_shot].Name : "loading";
            throw new TimeoutException(
                $"Shadow capture shot {name} did not finish: {_remote.Count} of {3 + NaviskoNpcs.Length} actors loaded, frame {_frame}.");
        }

        if (_shot < 0 || _frame == 0 && _shotSeconds == 0)
        {
            _shot = Math.Max(_shot, 0);
            if (_shot >= Shots.Length)
            {
                Exit();
                return;
            }

            _shotTime.Restart();
            var shot = Shots[_shot];
            _presentation.ApplyRendering(shot.Settings(new ClientSettings()));
            _renderer.SetTimeOfDay(shot.Hour);
            var spot = shot.AtLamp ? _lampSpot : _sunSpot;
            if (spot != _focus)
            {
                Place(spot);
            }
        }

        const float step = 1f / 60f;
        _seconds += step;
        _shotSeconds += step;
        if (_shot < Shots.Length && Shots[_shot].SunMoves)
        {
            _renderer.SetTimeOfDay(Shots[_shot].Hour + (float)_shotSeconds * HoursPerSecond);
        }

        _remote.Update(step);
        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_seconds), TimeSpan.FromSeconds(step)));
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shot < 0 || _shot >= Shots.Length || _remote.Count < 3 + NaviskoNpcs.Length || _shotSeconds < 2.0)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var shot = Shots[_shot];
        using var target = new RenderTarget2D(GraphicsDevice, Width, Height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
        GraphicsDevice.SetRenderTarget(target);
        var focus = shot.Npc >= 0 ? NpcSpot(shot.Npc) : _focus;
        var eye = shot.Close ? focus + new Vector3(35f, 45f, 55f) : focus + new Vector3(80f, 80f, 150f);
        if (shot.Orbit)
        {
            // A camera turning around the focus at a quarter turn in ten seconds, as a player turns it.
            eye = focus + Vector3.Transform(eye - focus, Matrix.CreateRotationY((float)_shotSeconds * MathHelper.PiOver2 / 10f));
        }

        _camera ??= new PristonCamera(eye, Vector3.Forward);
        _camera.SetScriptedView(GraphicsDevice, eye, focus + Vector3.Up * (shot.Close ? 30f : 25f));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        GraphicsDevice.SetRenderTarget(null);
        var pixels = _pixels;
        target.GetData(pixels);
        if (shot.Measure)
        {
            // The mean brightness of the frame and of the ground in front, per frame: a pulse shows as a repeating step.
            double all = 0d;
            double ground = 0d;
            for (var index = 0; index < pixels.Length; index++)
            {
                var luma = 0.2126 * pixels[index].R + 0.7152 * pixels[index].G + 0.0722 * pixels[index].B;
                all += luma;
                ground += index / Width >= Height * 7 / 10 ? luma : 0d;
            }

            Console.WriteLine($"Brightness {_frame} {all / pixels.Length:F3} {ground / (Width * (Height - Height * 7 / 10)):F3}");
            if (++_frame >= shot.Frames)
            {
                _frame = 0;
                _shotSeconds = 0;
                _shot++;
            }

            return;
        }

        for (var index = 0; index < pixels.Length; index++)
        {
            pixels[index].A = 255;
        }

        using var opaque = new Texture2D(GraphicsDevice, Width, Height);
        opaque.SetData(pixels);
        using (var stream = File.Create(Path.Combine(_output, $"{shot.Name}-{_frame}.png")))
        {
            opaque.SaveAsPng(stream, Width, Height);
        }

        // The change from the previous frame, eight times brighter: what trembles while nothing moves.
        if (_frame > 0 && _previous is not null)
        {
            var changed = 0;
            var difference = new Color[pixels.Length];
            for (var index = 0; index < pixels.Length; index++)
            {
                var delta = Math.Max(Math.Abs(pixels[index].R - _previous[index].R),
                    Math.Max(Math.Abs(pixels[index].G - _previous[index].G), Math.Abs(pixels[index].B - _previous[index].B)));
                changed += delta > 6 ? 1 : 0;
                var shade = (byte)Math.Min(255, delta * 8);
                difference[index] = new Color(shade, shade, shade, (byte)255);
            }

            opaque.SetData(difference);
            using var diffStream = File.Create(Path.Combine(_output, $"{shot.Name}-{_frame}-change.png"));
            opaque.SaveAsPng(diffStream, Width, Height);
            Console.WriteLine($"Shadows {shot.Name}-{_frame}: {changed} pixels changed from the previous frame");
        }

        // The next frame reads into the other buffer, so this one stays the previous frame.
        (_previous, _pixels) = (pixels, _previous ?? new Color[Width * Height]);
        Console.WriteLine($"Shadows {shot.Name}-{_frame}: captured");
        if (++_frame >= shot.Frames)
        {
            _previous = null;
            _frame = 0;
            _shotSeconds = 0;
            _shot++;
        }
    }

    protected override void UnloadContent() => _remote.Dispose();

    // The client's yaw for a server heading, mirrored in Z as the game scene mirrors every presence facing.
    private static float ClientFacing(int angle)
    {
        var facing = (angle & 4095) * (MathF.Tau / 4096f);
        return MathF.Atan2(MathF.Sin(facing), -MathF.Cos(facing));
    }

    private Vector3 NpcSpot(int index) => Floor(_collision, new Vector3(NaviskoNpcs[index].X, 5000f, NaviskoNpcs[index].Z));

    private static Vector3 Floor(PristonCollisionWorld collision, Vector3 point) =>
        collision.TryGetFloorHeight(point with { Y = point.Y + 500f }, out var height) ? point with { Y = height } : point;

    private static System.Numerics.Vector3 Numeric(Vector3 value) => new(value.X, value.Y, value.Z);

    private string Resolve(string relative) => AssetFiles.Resolve(Path.Combine(_assetRoot, relative.Replace('\\', '/')));
}
