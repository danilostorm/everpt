using PristonTale.Client.Net.Presence;
using PristonTale.Gameplay.Effects;
using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.Modifiers;

namespace PristonTale.Gameplay.UI.Widgets;

// Boosters and the skill-delay item run as premium timers but show as continuous effects: a keep icon in the
// running-effects bar whose gauge measures the time left against the item's full duration.
public sealed class HudPremiumKeeps
{
    private readonly List<StatusEntry> _keeps = new();
    private readonly List<StatusEntry> _merged = new();
    private long _revision = -1;

    // The player's statuses followed by the premium keeps; the list is reused between calls.
    public IReadOnlyList<StatusEntry> With(IReadOnlyList<StatusEntry> statuses, PremiumTimerSnapshot timers, double nowMs)
    {
        if (timers is not null && timers.Revision != _revision)
        {
            _revision = timers.Revision;
            _keeps.Clear();
            foreach (var timer in timers.Timers)
            {
                if (PremiumTimers.PausesOffline(timer.Kind))
                {
                    _keeps.Add(new StatusEntry
                    {
                        Code = ItemStatuses.PremiumKeepCode(timer.Kind),
                        SourceKind = EffectSourceKind.Item,
                        DurationMs = (int)Math.Min(int.MaxValue, timer.DurationSeconds * 1000L),
                        EndMs = nowMs + timer.RemainingSeconds * 1000d,
                        Stacks = 1,
                        Value = timer.Value
                    });
                }
            }
        }

        if (_keeps.Count == 0)
        {
            return statuses;
        }

        _merged.Clear();
        _merged.AddRange(statuses);
        foreach (var keep in _keeps)
        {
            if (keep.RemainingMs(nowMs) > 0)
            {
                _merged.Add(keep);
            }
        }

        return _merged;
    }
}
