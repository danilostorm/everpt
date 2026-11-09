namespace PristonTale.Application.World;

// The clip of the one action a player runs (swing, cast, eat, cheer, flinch) on the field clock: how long it has run
// against its length (at least 1 ms), and the time of its one event (a swing's hit, a potion's restore), which fires once.
internal struct ActionTimeline
{
    public float ElapsedMs { get; private set; }
    public float DurationMs { get; private set; }
    public float EventMs { get; private set; }
    public bool EventFired { get; private set; }

    public readonly bool Finished => ElapsedMs >= DurationMs;

    public static ActionTimeline Start(float durationMs, float eventMs = float.PositiveInfinity) =>
        new() { DurationMs = MathF.Max(1f, durationMs), EventMs = eventMs };

    // Returns the elapsed time before this step.
    public float Advance(float dtMs)
    {
        var before = ElapsedMs;
        ElapsedMs += dtMs;
        return before;
    }

    public void FireEvent() => EventFired = true;

    // Ends the clip now (a cheer that follows a capsule cuts its eat motion short).
    public void End() => ElapsedMs = DurationMs;

    // A shorter clip, never shorter than the time already run (a released charge).
    public void Shorten(float durationMs) => DurationMs = MathF.Max(ElapsedMs, MathF.Max(1f, durationMs));
}
