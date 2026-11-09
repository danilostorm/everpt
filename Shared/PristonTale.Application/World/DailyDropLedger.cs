using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

// Drops of each daily-limited item across the server today: an item whose limit is reached does not drop again until the
// server's local date changes. Fields step in parallel, so the ledger locks.
public sealed class DailyDropLedger(ItemCatalog items, TimeProvider clock)
{
    private readonly Dictionary<uint, int> _today = new();
    private readonly object _gate = new();
    private DateOnly _day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    // Counts one drop of the item and says whether it may fall; items without a limit always may.
    public bool TryTake(uint itemCode)
    {
        if (!items.TryGet(itemCode, out var definition) || definition.DailyDropLimit <= 0)
        {
            return true;
        }

        lock (_gate)
        {
            var day = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            if (day != _day)
            {
                _day = day;
                _today.Clear();
            }

            var dropped = _today.GetValueOrDefault(itemCode);
            if (dropped >= definition.DailyDropLimit)
            {
                return false;
            }

            _today[itemCode] = dropped + 1;
            return true;
        }
    }
}
