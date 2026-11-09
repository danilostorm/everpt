using System.Globalization;
using System.Text;

namespace PristonTale.Engine3D.Diagnostics;

public enum StreamingStep
{
    TextureUpload,
    MaterialSetup,
    StaticMeshUpload,
    AnimatedMeshUpload,
    ShadowAggregateUpload,
    PreparationWait,
    Publish
}

public readonly record struct StreamingStepTotals(int Count, double TotalMilliseconds, double MaximumMilliseconds, long Bytes);

// Main-thread cost of each kind of incremental stage-publication step, used to find the step that breaks the
// per-frame upload budget. PreparationWait counts frames spent polling worker tasks, not their CPU time.
public sealed class PristonStreamingStats
{
    private static readonly int StepCount = Enum.GetValues<StreamingStep>().Length;
    private readonly StreamingStepTotals[] _totals = new StreamingStepTotals[StepCount];

    public static PristonStreamingStats Empty { get; } = new(string.Empty);

    public PristonStreamingStats(string source)
    {
        Source = source;
    }

    public string Source { get; }

    public StreamingStepTotals this[StreamingStep step] => _totals[(int)step];

    internal void Record(StreamingStep step, double milliseconds, long bytes = 0)
    {
        var current = _totals[(int)step];
        _totals[(int)step] = new StreamingStepTotals(
            current.Count + 1,
            current.TotalMilliseconds + milliseconds,
            Math.Max(current.MaximumMilliseconds, milliseconds),
            current.Bytes + bytes);
    }

    public Dictionary<string, StreamingStepTotals> ToDictionary()
    {
        var result = new Dictionary<string, StreamingStepTotals>(StepCount);
        foreach (var step in Enum.GetValues<StreamingStep>())
        {
            if (_totals[(int)step].Count > 0)
            {
                result.Add(step.ToString(), _totals[(int)step]);
            }
        }

        return result;
    }

    public string Summary()
    {
        var builder = new StringBuilder();
        foreach (var step in Enum.GetValues<StreamingStep>())
        {
            var totals = _totals[(int)step];
            if (totals.Count == 0)
            {
                continue;
            }

            builder.Append(CultureInfo.InvariantCulture,
                $"{step} n={totals.Count} total={totals.TotalMilliseconds:F1}ms max={totals.MaximumMilliseconds:F2}ms bytes={totals.Bytes}; ");
        }

        return builder.ToString().TrimEnd();
    }
}
