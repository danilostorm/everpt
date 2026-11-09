namespace PristonTale.Engine3D.Diagnostics;

public sealed class FramePacer
{
    private long _deadline;
    private int _limit;

    public long Schedule(long now, long frequency, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        if (limit == 0)
        {
            _limit = 0;
            _deadline = now;
            return now;
        }

        var interval = Math.Max(1, (long)Math.Ceiling((double)frequency / limit));
        if (_limit != limit || _deadline == 0 || now - _deadline >= interval)
        {
            _deadline = now;
        }

        _limit = limit;
        _deadline += interval;
        return _deadline;
    }
}
