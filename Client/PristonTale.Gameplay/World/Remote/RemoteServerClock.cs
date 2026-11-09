namespace PristonTale.Gameplay.World.Remote;

// Maps the local frame clock onto the server simulation clock carried by the PresenceClock extension. The offset follows
// the earliest-arriving packets quickly and late ones slowly, so network jitter is absorbed by the playback delay instead of
// stretching the timeline. Without the extension every sample is stamped with its local arrival time.
public sealed class RemoteServerClock
{
    private const double ResyncMs = 1000;
    private const double EarlyArrivalRate = 0.25;
    private const double LateArrivalRate = 0.02;

    private double _offsetMs;

    public double LocalMs { get; private set; }

    public bool Synchronised { get; private set; }

    public double NowMs => LocalMs + _offsetMs;

    public void Advance(double elapsedMs)
    {
        if (double.IsFinite(elapsedMs) && elapsedMs > 0)
        {
            LocalMs += elapsedMs;
        }
    }

    public void Observe(double serverMs)
    {
        if (!double.IsFinite(serverMs))
        {
            return;
        }

        var offset = serverMs - LocalMs;
        if (!Synchronised || Math.Abs(offset - _offsetMs) > ResyncMs)
        {
            _offsetMs = offset;
            Synchronised = true;
            return;
        }

        var rate = offset > _offsetMs ? EarlyArrivalRate : LateArrivalRate;
        _offsetMs += (offset - _offsetMs) * rate;
    }

    // A server-stamped sample seen before any clock-bearing update synchronises the clock, so it never lands on the local timeline.
    public double Stamp(double? serverMs)
    {
        if (serverMs is not { } value || !double.IsFinite(value))
        {
            return NowMs;
        }

        if (!Synchronised)
        {
            Observe(value);
        }

        return value;
    }
}
