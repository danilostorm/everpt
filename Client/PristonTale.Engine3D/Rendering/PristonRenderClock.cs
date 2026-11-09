namespace PristonTale.Engine3D.Rendering;

internal sealed class PristonRenderClock
{
    private long _remainingTicks;

    public uint Milliseconds { get; private set; }

    public void Advance(TimeSpan elapsed)
    {
        if (elapsed.Ticks <= 0)
        {
            return;
        }

        var milliseconds = elapsed.Ticks / TimeSpan.TicksPerMillisecond;
        _remainingTicks += elapsed.Ticks % TimeSpan.TicksPerMillisecond;
        milliseconds += _remainingTicks / TimeSpan.TicksPerMillisecond;
        _remainingTicks %= TimeSpan.TicksPerMillisecond;
        Milliseconds = unchecked(Milliseconds + (uint)milliseconds);
    }
}
