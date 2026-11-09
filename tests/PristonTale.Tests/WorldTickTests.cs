using PristonTale.Server.World;

namespace PristonTale.Tests;

// The world tick's timeline: ticks keep their absolute times, a late tick is caught up and a stall is skipped.
internal static class WorldTickTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("the tick schedule keeps absolute times, runs owed ticks back to back and skips a stall past its catch-up", ScheduleAsync);
    }

    private static Task ScheduleAsync()
    {
        var schedule = new TickSchedule(period: 10, maxCatchUp: 5, start: 0);
        Assert.True(schedule.WaitTicks(4) == 6, "the first tick was not due a period after the start");
        schedule.Advance(13);
        Assert.True(schedule.WaitTicks(13) == 7 && schedule.Late == 0, "a tick woken a little late moved the timeline");
        schedule.Advance(35);
        Assert.True(schedule.Late == 1 && schedule.WaitTicks(35) <= 0, "a tick a period late was not counted, or its owed tick was not due at once");
        schedule.Advance(35);
        Assert.True(schedule.WaitTicks(35) == 5, "the caught-up ticks left the timeline");
        schedule.Advance(200);
        Assert.True(schedule.Skipped == 16 && schedule.WaitTicks(200) == 10, "a stall past the catch-up was replayed instead of skipped");
        return Task.CompletedTask;
    }
}
