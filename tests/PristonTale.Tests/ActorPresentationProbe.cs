using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.Characters;

namespace PristonTale.Tests;

internal sealed class ActorPresentationProbe : Game
{
    private readonly string _root;
    private readonly string _output;
    private readonly bool _jobHeads;
    private PristonRenderer _renderer = null!;
    private PristonCamera _camera = null!;
    private Task<PristonActor[]> _preparation = null!;
    private readonly List<(ActorHandle Handle, Matrix World)> _actors = [];
    private readonly List<double> _updateTimes = [];
    private int _frames;
    private int _published;
    private readonly Stopwatch _timeout = Stopwatch.StartNew();

    public ActorPresentationProbe(string root, string content, string output, bool jobHeads = false)
    {
        _root = root;
        _output = output;
        _jobHeads = jobHeads;
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
        _renderer = new PristonRenderer(GraphicsDevice, Content) { BackgroundColor = new Color(42, 45, 49) };
        _camera = new PristonCamera(Vector3.Zero, Vector3.Forward,
            _jobHeads ? new PristonCameraSettings { FieldOfViewDegrees = 55 } : PristonCameraSettings.Default);
        _preparation = Task.Run(async () =>
        {
            var loader = new PristonActorLoader();
            var actors = new List<PristonActor>();
            foreach (var relative in AssetPaths())
            {
                var path = Path.Combine(_root, relative.Replace('\\', '/'));
                var actor = loader.Load(_root, path);
                await _renderer.PrepareActorResourcesAsync(actor);
                actors.Add(actor);
            }

            return actors.ToArray();
        });
    }

    private IEnumerable<string> AssetPaths()
    {
        if (_jobHeads)
        {
            var classes = new CharacterClassRegistry();
            foreach (var playerClass in new[] { PlayerClass.Mechanician, PlayerClass.Pikeman })
            {
                for (var tier = 0; tier < 4; tier++)
                {
                    yield return classes.Get(playerClass).Definition.DefaultVisuals.Body.InxExtensionPath;
                    yield return CharacterHead.Resolve(playerClass, tier, 0);
                }
            }

            yield break;
        }

        foreach (var name in new[] { "TN-003", "TN-004", "TN-007", "TN-010", "TN-001", "TN-005" })
        {
            yield return $"char/npc/{name}/{name}.inx";
        }
    }

    protected override void Update(GameTime gameTime)
    {
        if (_timeout.Elapsed > TimeSpan.FromSeconds(90))
        {
            throw new TimeoutException("NPC presentation probe did not finish.");
        }

        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("NPC preparation failed.", _preparation.Exception);
        }

        var actors = _preparation.Result;
        if (_published < actors.Length)
        {
            var position = _jobHeads
                ? new Vector3((_published / 2 % 4 - 1.5f) * 75, 0, _published / 8 * 90)
                : new Vector3((_published % 3 - 1) * 90, 0, _published / 3 * 110);
            var world = Matrix.CreateRotationY(MathHelper.Pi) * Matrix.CreateTranslation(position);
            var handle = _renderer.AddActor(new PristonActorInstance(actors[_published], world, MotionCode.Stand));
            _actors.Add((handle, world));
            if (_jobHeads)
            {
                Console.WriteLine($"Actor {_published}: {actors[_published].Mesh.Path}; position={position}; handle={handle}");
            }
            _published++;
            return;
        }

        var started = Stopwatch.GetTimestamp();
        foreach (var actor in _actors)
        {
            _renderer.SetActorWorld(actor.Handle, actor.World);
        }

        _renderer.Update(new GameTime(TimeSpan.FromSeconds(_frames / 60d), TimeSpan.FromSeconds(1d / 60)));
        if (_frames >= 60)
        {
            _updateTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        _frames++;
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_frames == 0)
        {
            GraphicsDevice.Clear(Color.Black);
            return;
        }

        var cameraPosition = _jobHeads ? new Vector3(0, 115, -300) : new Vector3(0, 195, -305);
        _camera.SetScriptedView(GraphicsDevice, cameraPosition, new Vector3(0, 20, 48));
        _renderer.Draw(_camera);
        _renderer.ComposeScene();
        if (_frames >= 180)
        {
            // The scene drawn once more into an 8-bit target and read back (reading the presented back buffer is not
            // reliable on the Vulkan runtime).
            var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
            var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
            using var target = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            GraphicsDevice.SetRenderTarget(target);
            _renderer.Draw(_camera);
            _renderer.ComposeScene();
            GraphicsDevice.SetRenderTarget(null);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_output))!);
            using (var stream = File.Create(_output))
            {
                target.SaveAsPng(stream, width, height);
            }

            _updateTimes.Sort();
            Console.WriteLine($"Actor probe: {_actors.Count} actors; GPU={GraphicsDevice.Adapter.DeviceName}; renderer Update p95={_updateTimes[(int)(_updateTimes.Count * 0.95)]:F3}ms");
            Console.WriteLine($"Capture: {_output}");
            Exit();
        }
    }

    protected override void UnloadContent()
    {
        _renderer.Dispose();
    }
}
