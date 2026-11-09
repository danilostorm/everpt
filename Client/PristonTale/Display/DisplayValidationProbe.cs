using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PristonTale.Engine3D.Diagnostics;
using PristonTale.Engine3D.IO;
using PristonTale.Engine3D.Rendering;

namespace PristonTale.Display;

internal sealed class DisplayValidationProbe : Game
{
    private sealed record ScenarioResult(ClientSettings Requested, string Effective, double FramesPerSecond, double[] IntervalsMilliseconds);
    private readonly GraphicsDeviceManager _graphics;
    private readonly PresentationSettings _settings = new();
    private readonly ClientSettingsStore _store;
    private readonly List<ScenarioResult> _results = new();
    private readonly List<double> _intervals = new();
    private PresentationController _controller;
    private int _phase;
    private int _frames;
    private long _previousFrame;
    private ClientSettings _requested;
    private readonly string _output;
    private readonly ClientSettings[] _scenarios =
    [
        new() { MaxFps = 60, MultiSampleCount = 0 },
        new() { MaxFps = 120, MultiSampleCount = 0 },
        new() { MaxFps = 0, MultiSampleCount = 0 },
        new() { MaxFps = 60, MultiSampleCount = 0, WindowWidth = 1024, WindowHeight = 768 },
        new() { MaxFps = 60, MultiSampleCount = 0, WindowMode = WindowMode.Borderless },
        new() { MaxFps = 60, MultiSampleCount = 0, WindowMode = WindowMode.Fullscreen, RefreshRate = 999 },
        new() { MaxFps = 60, MultiSampleCount = 0, WindowWidth = 800, WindowHeight = 600 }
    ];

    public DisplayValidationProbe(string output)
    {
        _output = Path.GetFullPath(output);
        _store = new ClientSettingsStore(_output + ".settings.json", ConsoleClientLog.Instance);
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        _settings.Requested = _scenarios[0];
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _controller = new PresentationController(_settings, _store, _graphics, Window);
        _requested = _settings.Requested;
    }

    protected override void Update(GameTime gameTime)
    {
        var now = Stopwatch.GetTimestamp();
        if (_frames >= 20 && _previousFrame != 0)
        {
            _intervals.Add(Stopwatch.GetElapsedTime(_previousFrame, now).TotalMilliseconds);
        }

        _previousFrame = now;
        _frames++;
        if (_frames < 100)
        {
            return;
        }

        var fps = _intervals.Count * 1000d / _intervals.Sum();
        if (_requested.MaxFps > 0 && fps > _requested.MaxFps * 1.05)
        {
            throw new InvalidOperationException($"Pacing exceeded {_requested.MaxFps}: measured {fps:F2} FPS.");
        }

        var parameters = GraphicsDevice.PresentationParameters;
        if (_requested.WindowMode == WindowMode.Windowed
            && (parameters.BackBufferWidth != _requested.WindowWidth || parameters.BackBufferHeight != _requested.WindowHeight))
        {
            throw new InvalidOperationException("Window/backbuffer dimensions disagree with the requested window size.");
        }

        _results.Add(new ScenarioResult(_requested, _settings.EffectiveDisplay, fps, _intervals.ToArray()));
        ConsoleClientLog.Instance.Info($"[DisplayProbe] phase={_phase}; {_settings.EffectiveDisplay}; FPS={fps:F2}");
        _phase++;
        if (_phase == _scenarios.Length)
        {
            Exit();
            return;
        }

        _requested = _scenarios[_phase];
        _settings.Requested = _requested;
        _intervals.Clear();
        _frames = 0;
        _previousFrame = 0;
    }

    protected override void Draw(GameTime gameTime) => GraphicsDevice.Clear(Color.CornflowerBlue);

    protected override void EndDraw()
    {
        base.EndDraw();
        _controller.UpdateBoundary();
        if (_settings.AwaitingConfirmation)
        {
            _settings.ConfirmRequested = true;
            _controller.UpdateBoundary();
        }

        _controller.Pace(active: true);
    }

    public async Task WriteReportAsync()
    {
        await _store.CompleteAsync();
        if (_results.Count != _scenarios.Length || _store.LastError.Length > 0)
        {
            throw new InvalidOperationException("Display validation did not complete all scenarios and persistence.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_output));
        var report = new { OperatingSystem = Environment.OSVersion.ToString(), Scenarios = _results };
        await File.WriteAllTextAsync(_output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }
}
