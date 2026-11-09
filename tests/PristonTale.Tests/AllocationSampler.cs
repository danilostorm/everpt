using System.Collections.Concurrent;
using System.Diagnostics.Tracing;

namespace PristonTale.Tests;

// Samples the runtime's allocation ticks (one event per ~100 KiB allocated) by type, to find what a benchmark allocates.
internal sealed class AllocationSampler : EventListener
{
    private const EventKeywords GcKeyword = (EventKeywords)0x1;
    private readonly ConcurrentDictionary<string, long> _bytes = new();
    private volatile bool _recording;

    public void Start() => _recording = true;

    public void Stop() => _recording = false;

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
        {
            EnableEvents(source, EventLevel.Verbose, GcKeyword);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs data)
    {
        if (!_recording || data.EventName is null || !data.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || data.PayloadNames is null)
        {
            return;
        }

        var typeIndex = data.PayloadNames.IndexOf("TypeName");
        var amountIndex = data.PayloadNames.IndexOf("AllocationAmount64");
        if (typeIndex < 0 || data.Payload is null)
        {
            return;
        }

        var type = data.Payload[typeIndex] as string ?? "?";
        var amount = amountIndex >= 0 && data.Payload[amountIndex] is ulong value ? (long)value : 100_000;
        _bytes.AddOrUpdate(type, amount, (_, total) => total + amount);
    }

    public void Print(int ticks)
    {
        foreach (var (type, bytes) in _bytes.OrderByDescending(pair => pair.Value).Take(15))
        {
            Console.WriteLine($"    {bytes / 1024.0 / ticks,8:F1} KiB/tick  {type}");
        }
    }
}
