namespace PristonTale.Application.Services;

// The waits of a background writer between attempts storage refuses: first, then twice the last wait up to Ceiling. A
// write storage takes starts over from first.
internal sealed class RetryBackoff(TimeSpan? first = null)
{
    private static readonly TimeSpan DefaultFirst = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    private readonly TimeSpan _first = first ?? DefaultFirst;
    private TimeSpan _next = first ?? DefaultFirst;

    public TimeSpan Next()
    {
        var wait = _next;
        _next = _next * 2 < Ceiling ? _next * 2 : Ceiling;
        return wait;
    }

    public void Reset() => _next = _first;
}
