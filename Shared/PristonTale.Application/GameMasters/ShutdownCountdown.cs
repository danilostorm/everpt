using System.Globalization;
using PristonTale.Application.Chat;

namespace PristonTale.Application.GameMasters;

// The timing of a game master's shutdown: eight minutes of warnings, then every client is asked to save and quit, and the
// server stops a minute later.
public sealed record ShutdownSchedule(TimeSpan Countdown, TimeSpan NoticeGap, TimeSpan Reminder, TimeSpan LastMinuteReminder, TimeSpan Grace)
{
    public static readonly ShutdownSchedule Standard = new(TimeSpan.FromMinutes(8), TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1));
}

public static class ShutdownCountdown
{
    public static readonly IReadOnlyList<string> OpeningNotices =
    [
        "Notice> May I have your attention please",
        "Notice> The servers will be going down soon due to maintenance",
        "Notice> Please exit the game",
        "Notice> Servers are going down soon",
        "Notice> Please exit the game. Thank you"
    ];

    public static string Reminder(TimeSpan left) => string.Format(CultureInfo.InvariantCulture,
        "Shutdown> {0} mins {1} secs left to exit. Please exit the game", (int)left.TotalMinutes, left.Seconds);

    // The opening notices one after another, then a reminder every half minute (every five seconds in the last minute);
    // when the time is up every client is told to close, and stop runs after the grace time.
    public static async Task RunAsync(ChatDirectory directory, ShutdownSchedule schedule, Action stop, TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        clock ??= TimeProvider.System;
        var end = clock.GetUtcNow() + schedule.Countdown;
        var opening = 0;
        while (true)
        {
            var left = end - clock.GetUtcNow();
            if (left < TimeSpan.Zero)
            {
                break;
            }

            TimeSpan wait;
            if (opening < OpeningNotices.Count)
            {
                ServerNotices.BroadcastRaw(directory, OpeningNotices[opening++]);
                wait = schedule.NoticeGap;
            }
            else
            {
                ServerNotices.BroadcastRaw(directory, Reminder(left));
                wait = left.TotalMinutes < 1 ? schedule.LastMinuteReminder : schedule.Reminder;
            }

            await Task.Delay(wait, clock, cancellationToken);
        }

        foreach (var member in directory.Members)
        {
            member.Link.RequestClose();
        }

        await Task.Delay(schedule.Grace, clock, cancellationToken);
        stop();
    }
}
