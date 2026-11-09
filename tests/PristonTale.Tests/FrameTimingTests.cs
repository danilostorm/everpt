using System.Diagnostics;
using PristonTale.Engine3D.Diagnostics;

namespace PristonTale.Tests;

internal static class FrameTimingTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("presentation telemetry uses wall time and includes all updates before a draw", TimingAsync);
    }

    private static Task TimingAsync()
    {
        var timing = new FrameTiming();
        var now = Stopwatch.Frequency * 10;
        var frameTicks = Stopwatch.Frequency / 120;
        var millisecond = Stopwatch.Frequency / 1000;
        for (var index = 0; index < 500; index++)
        {
            now += frameTicks;
            timing.RecordUpdate(millisecond, 16);
            timing.RecordUpdate(millisecond, 32);
            timing.RecordDraw(now - millisecond, now, 64);
            timing.RecordPresentation(5 * millisecond, 8);
            timing.RecordPacing(2 * millisecond);
        }

        Assert.True(timing.TrySample(now, out var sample), "no frame timing sample");
        Assert.True(Math.Abs(sample.FramesPerSecond - 120) < 0.1 && sample.FramesOver60HzBudget == 0, "FPS is not based on draw intervals");
        Assert.True(Math.Abs(sample.WorkP95Ms - 3) < 0.01, "multiple updates before one draw were lost");
        Assert.True(Math.Abs(sample.PacingP95Ms - 2) < 0.01, "pacing was lost or added to Update/Draw work");
        Assert.True(sample.AllocatedBytesPerFrame == 120 && sample.FrameP99Ms >= sample.FrameP95Ms, "allocation accounting or percentile order changed");
        Assert.True(Math.Abs(sample.PresentP95Ms - 5) < 0.01 && sample.PresentP99Ms >= sample.PresentP95Ms,
            "presentation wait was lost or counted as Update/Draw work");
        for (var index = 0; index < 360; index++)
        {
            now += Stopwatch.Frequency / 30;
            timing.RecordDraw(now - millisecond, now);
        }

        Assert.True(timing.TrySample(now, out sample) && Math.Abs(sample.FramesPerSecond - 30) < 0.1
            && sample.FramesOver60HzBudget == 360 && sample.FrameP95Ms > 33 && sample.AllocatedBytesPerFrame == 0,
            "rolling window hid slow frames or retained stale samples");
        Assert.True(sample.PresentP95Ms == 0 && sample.PresentP99Ms == 0, "rolling window retained stale presentation waits");
        Assert.True(sample.PacingP95Ms == 0, "rolling window retained stale pacing waits");
        return Task.CompletedTask;
    }
}
