using PristonTale.Application.World;

namespace PristonTale.Tests;

// Ticks a world until an operation it serves completes. The operation's continuations run on the thread pool, so a
// millisecond passes between ticks for each released step to queue the next one. nextTick numbers the ticks for a
// fixture that keeps its own clock; without it every run counts from 0.
internal static class WorldPump
{
    private const float TickSeconds = 0.05f;
    private const int MaxTicks = 2000;

    public static async Task RunAsync(WorldServer world, Task task, Func<uint>? nextTick = null)
    {
        uint tick = 0;
        for (var step = 0; !task.IsCompleted && step < MaxTicks; step++)
        {
            world.Tick(TickSeconds, nextTick?.Invoke() ?? tick++);
            await Task.Delay(1);
        }

        await task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    public static async Task<T> RunAsync<T>(WorldServer world, Task<T> task, Func<uint>? nextTick = null)
    {
        await RunAsync(world, (Task)task, nextTick);
        return await task;
    }
}
