using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Xna.Framework;
using PristonTale.Engine3D.Formats;
using PristonTale.Engine3D.Physics;
using PristonTale.Engine3D.Rendering;
using PristonTale.Simulation.World.Fields;
using PristonTale.Gameplay.World.Streaming;

namespace PristonTale.Tests;

internal sealed class FieldStreamingProbe : Game
{
    private readonly string _assetRoot;
    private readonly GraphicsDeviceManager _graphics;
    private PristonRenderer _renderer = null!;
    private Task<(PristonStage Stage, PristonModel[] Models)> _preparation = null!;
    private IEnumerator<RenderStageHandle>? _upload;
    private readonly Stopwatch _elapsed = new();
    private readonly List<double> _updateTimes = new();
    private readonly int _maximumSteps;
    private int _steps;
    private bool _finished;

    public FieldStreamingProbe(string assetRoot, string contentRoot, bool baselineBudget = false)
    {
        _assetRoot = assetRoot;
        _maximumSteps = baselineBudget ? 8 : FieldStreamingPolicy.MaximumUploadSteps;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = false
        };
        Content = new AbsoluteContentManager(Services, contentRoot);
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(1d / 60d);
    }

    protected override void LoadContent()
    {
        Console.WriteLine($"STREAM GPU={GraphicsDevice.Adapter.DeviceName} steps/frame={_maximumSteps}");
        _renderer = new PristonRenderer(GraphicsDevice, Content);
        _renderer.ConfigureFieldLighting(8f, 1.2f, null, 1f, 1f);
        _elapsed.Start();
        _preparation = Task.Run(async () =>
        {
            var field = RicartenField.Definition;
            var reader = new PristonSmdReader();
            var stage = reader.ReadStage(Resolve(field.StageSmd));
            var models = field.StageObjectSmds.Select(path => reader.ReadModel(Resolve(path))).ToArray();
            _ = new PristonCollisionWorld(stage);
            await _renderer.PrepareStageTexturesAsync(stage, models);
            Console.WriteLine($"STREAM CPU ready={_elapsed.Elapsed.TotalMilliseconds:F1}ms");
            return (stage, models);
        });
    }

    protected override void Update(GameTime gameTime)
    {
        if (_finished)
        {
            return;
        }

        if (!_preparation.IsCompleted)
        {
            return;
        }

        if (!_preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Field preparation failed.", _preparation.Exception);
        }

        _upload ??= _renderer.AddStageIncrementally(_preparation.Result.Stage, _preparation.Result.Models);
        var started = Stopwatch.GetTimestamp();
        for (var step = 0; step < _maximumSteps; step++)
        {
            Assert.True(_upload.MoveNext(), "upload ended without a stage");
            _steps++;
            if (_upload.Current is not null)
            {
                _finished = true;
                break;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= FieldStreamingPolicy.UploadBudgetMilliseconds)
            {
                break;
            }
        }

        _updateTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        if (_finished)
        {
            _updateTimes.Sort();
            Console.WriteLine($"STREAM ready={_elapsed.Elapsed.TotalMilliseconds:F1}ms frames={_updateTimes.Count} steps={_steps}");
            Console.WriteLine($"STREAM upload Update p95={_updateTimes[(int)(_updateTimes.Count * 0.95)]:F3}ms max={_updateTimes[^1]:F3}ms");
            ReportGeometry(_upload.Current ?? throw new InvalidOperationException("Published stage missing."));
            _upload.Dispose();
            _upload = null;
            Exit();
        }

        if (_elapsed.Elapsed > TimeSpan.FromMinutes(2))
        {
            throw new TimeoutException("Field publication exceeded two minutes.");
        }
    }

    private static void ReportGeometry(RenderStageHandle stage)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var batch in stage.Batches)
        {
            if (!batch.IsSkinned)
            {
                hash.AppendData(MemoryMarshal.AsBytes(batch.Vertices.AsSpan()));
            }
            else
            {
                var bounds = new[] { batch.SkinnedBounds.Min, batch.SkinnedBounds.Max };
                hash.AppendData(MemoryMarshal.AsBytes(bounds.AsSpan()));
                hash.AppendData(MemoryMarshal.AsBytes(batch.BonePalette.AsSpan()));
            }
        }

        Console.WriteLine($"STREAM batches={stage.Batches.Count} triangles={stage.Batches.Sum(batch => batch.TriangleCount)} " +
            $"geometryMetadataSHA256={Convert.ToHexString(hash.GetHashAndReset())}");
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
    }

    protected override void UnloadContent()
    {
        _upload?.Dispose();
        _renderer.Dispose();
    }

    private string Resolve(string path)
    {
        return Path.Combine(_assetRoot, path.Replace('\\', Path.DirectorySeparatorChar));
    }
}
