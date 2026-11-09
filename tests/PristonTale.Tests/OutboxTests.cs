using System.Numerics;
using PristonTale.Application.World;
using PristonTale.Gameplay.UI;
using PristonTale.Contracts.Motion;
using PristonTale.Contracts.World;
using PristonTale.Contracts.Combat;

namespace PristonTale.Tests;

internal static class OutboxTests
{
    public static IEnumerable<(string, Func<Task>)> Cases()
    {
        yield return ("outbox coalesces positions while preserving every event in order", CoalescingAsync);
        yield return ("a reader that falls past the event capacity loses the unread events, keeps its stream and is told so", SaturationAsync);
        yield return ("unchanged presence is suppressed and changed vitals remain visible", PresenceAsync);
        yield return ("HUD remains inside 4:3, widescreen and ultrawide viewports", LayoutAsync);
        yield return ("normal-rate outboxes share immutable event batches without cross-subscriber mutation", SharedBatchAsync);
    }

    private static Task SharedBatchAsync()
    {
        var first = new WorldOutbox();
        var second = new WorldOutbox();
        var events = new[] { new CombatEventView(1, 2, CombatEventKind.Hit, damage: 7) };
        var published = new WorldSnapshot(1, [], events);
        first.Writer.TryWrite(published);
        second.Writer.TryWrite(published);
        Assert.True(first.Reader.TryRead(out var delivered) && ReferenceEquals(delivered.Events, events), "single batch was copied per subscriber");
        first.Writer.TryWrite(new WorldSnapshot(2, [], [new CombatEventView(1, 2, CombatEventKind.Hit, damage: 3)]));
        first.Reader.TryRead(out _);
        Assert.True(second.Reader.TryRead(out var untouched) && ReferenceEquals(untouched.Events, events)
            && untouched.Events[0].Damage == 7, "another subscriber changed a published batch");
        return Task.CompletedTask;
    }

    private static Task CoalescingAsync()
    {
        var outbox = new WorldOutbox();
        for (uint tick = 1; tick <= 100; tick++)
        {
            Assert.True(outbox.Writer.TryWrite(new WorldSnapshot(tick, [],
                [new CombatEventView(tick, 1, CombatEventKind.Hit, damage: (int)tick)])), "outbox rejected bounded event count");
        }

        Assert.True(outbox.Reader.TryRead(out var snapshot) && snapshot.ServerTick == 100, "obsolete position roster retained");
        Assert.True(snapshot.Events.Length == 100, "coalescing discarded events");
        for (var index = 0; index < snapshot.Events.Length; index++)
        {
            Assert.True(snapshot.Events[index].Damage == index + 1, "event order changed");
        }

        Assert.True(!outbox.Reader.TryRead(out _), "outbox replayed a consumed batch");
        return Task.CompletedTask;
    }

    private static Task SaturationAsync()
    {
        var outbox = new WorldOutbox(2);
        WorldSnapshot Hit(uint tick, int damage) => new(tick, [], [new CombatEventView(1, 2, CombatEventKind.Hit, damage: damage)]);
        Assert.True(outbox.Writer.TryWrite(Hit(1, 1)) && outbox.Writer.TryWrite(Hit(2, 2)) && outbox.Writer.TryWrite(Hit(3, 3)),
            "a reader that fell behind lost its stream");
        Assert.True(outbox.Reader.TryRead(out var behind) && behind is { ServerTick: 3, EventsLost: true } && behind.Events is [{ Damage: 3 }],
            "the unread events were kept past the capacity, or the reader was not told they were lost");
        Assert.True(outbox.Writer.TryWrite(Hit(4, 4)) && outbox.Reader.TryRead(out var next) && !next.EventsLost && next.Events is [{ Damage: 4 }],
            "a reader that caught up was still told events were lost");
        return Task.CompletedTask;
    }

    private static Task PresenceAsync()
    {
        EntityView View(int life, int maxLife = 10)
        {
            return new EntityView(1, EntityKind.Player, false, Vector3.Zero, 0, MotionState.Stand, 1, life, maxLife, default);
        }

        var presence = new SubscriberPresence(1);
        Assert.True(presence.Diff([View(10)]).Spawns.Count == 1, "initial spawn missing");
        Assert.True(presence.Diff([View(10)]).States.Count == 0, "idle entity emitted an unchanged state");
        Assert.True(presence.Diff([View(9)]).States.Count == 1, "HP change suppressed");
        Assert.True(presence.Diff([View(9, 20)]).Spawns.Count == 1, "max HP change omitted from descriptor refresh");
        Assert.True(presence.Diff([]).Removed.Count == 1 && presence.Diff([]).Removed.Count == 0, "removal repeated or lost");
        return Task.CompletedTask;
    }

    private static Task LayoutAsync()
    {
        foreach (var resolution in new[] { (800, 600), (1024, 768), (1280, 720), (1920, 1080), (3440, 1440) })
        {
            var layout = HudLayout.Create(resolution.Item1, resolution.Item2);
            var bar = layout.Bottom(new Microsoft.Xna.Framework.Rectangle(288, 472, 512, 128));
            foreach (var rectangle in new[] { bar, PristonTale.Gameplay.UI.Widgets.HudMinimap.Box(layout) })
            {
                Assert.True(rectangle.Left >= 0 && rectangle.Top >= 0 && rectangle.Right * layout.Scale <= resolution.Item1
                    && rectangle.Bottom * layout.Scale <= resolution.Item2, "HUD escaped viewport");
            }
        }

        return Task.CompletedTask;
    }
}
