using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;

namespace PristonTale.Tests;

// What decoding the presence of a crowd costs the client: the states of a message go into a list the scene gives back,
// so a tick costs the update object and nothing per entity.
internal static class PresenceDecodeTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("decoding a crowded tick of moving entities allocates no memory per entity once its lists are given back", CrowdAsync);
    }

    private const int Crowd = 150;
    private const int MaxBytesPerState = 8;
    private const int WarmUpTicks = 30;
    private const int MeasuredTicks = 50;
    // The share of the measured packets decoded first, to fill the decoder's pools.
    private const int PoolFillShare = 5;

    private static EntityView[] Tick(uint tick) =>
    [
        new(1, EntityKind.Player, false, Vector3.Zero, 0f, MotionState.Stand, 1, 100, 100, default),
        .. Enumerable.Range(0, Crowd).Select(index => new EntityView((ulong)(100 + index), index % 2 == 0 ? EntityKind.Player : EntityKind.Monster, false,
            new Vector3(index % 15 * 20f + tick * 3f, 0f, index / 15 * 20f - tick * 2f), tick * 0.1f, MotionState.Walk, 1, 100, 100, default) { Revision = 1 })
    ];

    private static Task CrowdAsync()
    {
        var wire = new WireRoundTrip(1);
        for (uint tick = 1; tick <= WarmUpTicks; tick++)
        {
            wire.PushMerged(new WorldSnapshot(tick, Tick(tick), fieldId: 3));
        }

        var packets = Enumerable.Range(WarmUpTicks + 1, MeasuredTicks).SelectMany(tick => wire.Encode(new WorldSnapshot((uint)tick, Tick((uint)tick), fieldId: 3))).ToArray();
        // The first packets decoded fill the decoder's pools.
        Decode(wire, packets.AsSpan(0, packets.Length / PoolFillShare));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var states = Decode(wire, packets.AsSpan(packets.Length / PoolFillShare));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(states >= Crowd * MeasuredTicks / 10, $"only {states} states were decoded");
        Assert.True(allocated <= (long)states * MaxBytesPerState, $"{allocated} bytes for {states} states");
        return Task.CompletedTask;
    }

    // Decodes the packets, giving each update back as the scene does; the states decoded.
    private static int Decode(WireRoundTrip wire, ReadOnlySpan<byte[]> packets)
    {
        var states = 0;
        foreach (var packet in packets)
        {
            if (wire.Decoder.TryHandle(packet, out var update) && update is not null)
            {
                states += update.States.Count;
                wire.Decoder.Recycle(update);
            }
        }

        return states;
    }
}
