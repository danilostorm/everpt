namespace PristonTale.Engine3D.Rendering;

internal sealed class FixedRateRefreshScheduler
{
    private uint _lastTimestamp;
    private ulong _elapsedMilliseconds;
    private ulong _nextRefreshUnits;
    private int _rate;
    private bool _clockInitialized;
    private bool _hasRenderedContent;

    public void Invalidate()
    {
        _hasRenderedContent = false;
    }

    public bool ShouldRefresh(uint timestamp, int rate, bool enabled)
    {
        if (rate < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        AdvanceClock(timestamp);
        if (!enabled)
        {
            _hasRenderedContent = false;
            return false;
        }

        if (rate == 0)
        {
            _rate = 0;
            _hasRenderedContent = true;
            return true;
        }

        if (_rate != rate)
        {
            _rate = rate;
            _hasRenderedContent = false;
        }

        var currentUnits = _elapsedMilliseconds * (ulong)rate;
        if (!_hasRenderedContent)
        {
            _hasRenderedContent = true;
            _nextRefreshUnits = currentUnits + 1000;
            return true;
        }

        if (currentUnits < _nextRefreshUnits)
        {
            return false;
        }

        var elapsedIntervals = (currentUnits - _nextRefreshUnits) / 1000 + 1;
        _nextRefreshUnits += elapsedIntervals * 1000;
        return true;
    }

    private void AdvanceClock(uint timestamp)
    {
        if (!_clockInitialized)
        {
            _lastTimestamp = timestamp;
            _clockInitialized = true;
            return;
        }

        _elapsedMilliseconds += unchecked(timestamp - _lastTimestamp);
        _lastTimestamp = timestamp;
    }
}
