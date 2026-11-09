using System.Net;

namespace PristonTale.Server.Net;

// Fixed-window packet counter of one connection; called only from that connection's receive loop.
internal sealed class PacketRateLimiter(int limit, long windowMs)
{
    private long _windowStartMs;
    private int _count;
    private bool _started;

    public bool Admit(long nowMs)
    {
        if (limit <= 0 || windowMs <= 0)
        {
            return true;
        }

        if (!_started || nowMs - _windowStartMs >= windowMs)
        {
            _started = true;
            _windowStartMs = nowMs;
            _count = 0;
        }

        _count++;
        return _count <= limit;
    }
}

// Single-threaded token bucket; capacity 0 disables it.
internal sealed class TokenBucket(int capacity, int perSecond)
{
    private double _tokens = capacity;
    private long _lastMs;
    private bool _started;

    public bool TryTake(long nowMs)
    {
        if (capacity <= 0)
        {
            return true;
        }

        if (_started)
        {
            _tokens = Math.Min(capacity, _tokens + (Math.Max(0, nowMs - _lastMs) * perSecond / 1000.0));
        }

        _started = true;
        _lastMs = nowMs;
        if (_tokens < 1.0)
        {
            return false;
        }

        _tokens -= 1.0;
        return true;
    }
}

// Open connections per remote address, shared by the accept loop and the session cleanup.
internal sealed class ConnectionLimiter(int maxPerAddress)
{
    private readonly Dictionary<IPAddress, int> _counts = new();
    private readonly object _gate = new();

    public bool TryAcquire(IPAddress? address)
    {
        if (maxPerAddress <= 0 || address is null)
        {
            return true;
        }

        lock (_gate)
        {
            var count = _counts.GetValueOrDefault(address);
            if (count >= maxPerAddress)
            {
                return false;
            }

            _counts[address] = count + 1;
            return true;
        }
    }

    public void Release(IPAddress? address)
    {
        if (maxPerAddress <= 0 || address is null)
        {
            return;
        }

        lock (_gate)
        {
            if (!_counts.TryGetValue(address, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _counts.Remove(address);
            }
            else
            {
                _counts[address] = count - 1;
            }
        }
    }
}

// Failed logins per account and per address in fixed windows. Blocked attempts are refused before the password
// hash is computed, which bounds both guessing and the PBKDF2 cost an attacker can cause.
internal sealed class LoginThrottle(int perAccount, int perAddress, long windowMs)
{
    private const int MaxTrackedKeys = 65536;
    private readonly Dictionary<string, FailureWindow> _accounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<IPAddress, FailureWindow> _addresses = new();
    private readonly object _gate = new();

    private sealed class FailureWindow
    {
        public long StartMs;
        public int Failures;
    }

    public bool IsBlocked(IPAddress? address, string account, long nowMs)
    {
        lock (_gate)
        {
            return Exceeded(_accounts, account, perAccount, nowMs) || address is not null && Exceeded(_addresses, address, perAddress, nowMs);
        }
    }

    public void RecordFailure(IPAddress? address, string account, long nowMs)
    {
        lock (_gate)
        {
            Count(_accounts, account, perAccount, nowMs);
            if (address is not null)
            {
                Count(_addresses, address, perAddress, nowMs);
            }
        }
    }

    public void RecordSuccess(string account)
    {
        lock (_gate)
        {
            _accounts.Remove(account);
        }
    }

    // Accounts whose logins are refused now, with the time left until their window ends.
    public IReadOnlyList<(string Account, long RemainingMs)> BlockedAccounts(long nowMs)
    {
        lock (_gate)
        {
            return _accounts.Where(pair => Exceeded(_accounts, pair.Key, perAccount, nowMs))
                .Select(pair => (pair.Key, windowMs - (nowMs - pair.Value.StartMs)))
                .ToArray();
        }
    }

    // Forgets every failure, of accounts and addresses; returns how many accounts were blocked.
    public int Clear(long nowMs)
    {
        lock (_gate)
        {
            var blocked = _accounts.Keys.Count(key => Exceeded(_accounts, key, perAccount, nowMs));
            _accounts.Clear();
            _addresses.Clear();
            return blocked;
        }
    }

    // Forgets an account's failures; true when it was blocked.
    public bool Release(string account, long nowMs)
    {
        lock (_gate)
        {
            var blocked = Exceeded(_accounts, account, perAccount, nowMs);
            _accounts.Remove(account);
            return blocked;
        }
    }

    public void Prune(long nowMs)
    {
        lock (_gate)
        {
            Prune(_accounts, nowMs);
            Prune(_addresses, nowMs);
        }
    }

    private bool Exceeded<TKey>(Dictionary<TKey, FailureWindow> windows, TKey key, int limit, long nowMs) where TKey : notnull
    {
        return limit > 0 && windows.TryGetValue(key, out var window) && nowMs - window.StartMs < windowMs && window.Failures >= limit;
    }

    private void Count<TKey>(Dictionary<TKey, FailureWindow> windows, TKey key, int limit, long nowMs) where TKey : notnull
    {
        if (limit <= 0)
        {
            return;
        }

        if (!windows.TryGetValue(key, out var window))
        {
            if (windows.Count >= MaxTrackedKeys)
            {
                Prune(windows, nowMs);
                if (windows.Count >= MaxTrackedKeys)
                {
                    return;
                }
            }

            window = new FailureWindow { StartMs = nowMs };
            windows.Add(key, window);
        }
        else if (nowMs - window.StartMs >= windowMs)
        {
            window.StartMs = nowMs;
            window.Failures = 0;
        }

        window.Failures++;
    }

    private void Prune<TKey>(Dictionary<TKey, FailureWindow> windows, long nowMs) where TKey : notnull
    {
        foreach (var (key, window) in windows)
        {
            if (nowMs - window.StartMs >= windowMs)
            {
                windows.Remove(key);
            }
        }
    }
}

// At most one log line per interval for a noisy category, carrying the count of lines it replaced.
internal sealed class LogSampler(long intervalMs)
{
    private readonly object _gate = new();
    private long _nextMs;
    private bool _started;
    private int _suppressed;

    public bool TryEnter(long nowMs, out int suppressed)
    {
        lock (_gate)
        {
            if (_started && nowMs < _nextMs)
            {
                _suppressed++;
                suppressed = 0;
                return false;
            }

            _started = true;
            _nextMs = nowMs + intervalMs;
            suppressed = _suppressed;
            _suppressed = 0;
            return true;
        }
    }
}
