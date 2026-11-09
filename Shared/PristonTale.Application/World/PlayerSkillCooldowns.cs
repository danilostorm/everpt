namespace PristonTale.Application.World;

// Server-side cooldowns of one player on the field clock. A started cooldown survives field changes because the
// player object moves with the connection; only its remaining time is reported.
public sealed class PlayerSkillCooldowns
{
    private readonly Dictionary<uint, (long EndMs, int DurationMs)> _running = new();
    private readonly List<uint> _expired = new();

    public long Revision { get; private set; }

    public int RemainingMs(uint skillCode, long nowMs)
    {
        if (!_running.TryGetValue(skillCode, out var cooldown))
        {
            return 0;
        }

        var remaining = cooldown.EndMs - nowMs;
        return remaining > 0 ? (int)Math.Min(int.MaxValue, remaining) : 0;
    }

    public void Start(uint skillCode, int durationMs, long nowMs)
    {
        if (durationMs <= 0)
        {
            return;
        }

        _running[skillCode] = (nowMs + durationMs, durationMs);
        Revision++;
    }

    // The field clock restarts with each field instance; carry the remaining times to the new clock.
    public void Rebase(long oldNowMs, long newNowMs)
    {
        if (_running.Count == 0 || oldNowMs == newNowMs)
        {
            return;
        }

        foreach (var code in _running.Keys.ToArray())
        {
            var cooldown = _running[code];
            _running[code] = (cooldown.EndMs - oldNowMs + newNowMs, cooldown.DurationMs);
        }

        Revision++;
    }

    // The running cooldowns when they changed since the given revision; expired entries are dropped first.
    public SkillCooldownView[]? SnapshotIfChanged(long sentRevision, long nowMs)
    {
        _expired.Clear();
        foreach (var (code, cooldown) in _running)
        {
            if (cooldown.EndMs <= nowMs)
            {
                _expired.Add(code);
            }
        }

        foreach (var code in _expired)
        {
            _running.Remove(code);
        }

        if (_expired.Count > 0)
        {
            Revision++;
        }

        if (sentRevision == Revision)
        {
            return null;
        }

        var views = new SkillCooldownView[_running.Count];
        var index = 0;
        foreach (var (code, cooldown) in _running)
        {
            views[index++] = new SkillCooldownView(code, (int)Math.Max(0, cooldown.EndMs - nowMs), cooldown.DurationMs);
        }

        return views;
    }
}
