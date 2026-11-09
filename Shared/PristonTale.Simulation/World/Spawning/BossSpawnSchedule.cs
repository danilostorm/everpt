using PristonTale.Simulation.World.Ai;

namespace PristonTale.Simulation.World.Spawning;

public sealed class BossSpawnSchedule
{
    private readonly ISimRng _rng;
    private DateOnly _date;
    private DateTime _lastSlot = DateTime.MinValue;
    private TimeSpan? _lastPassElapsed;

    public int Minute { get; private set; }

    public BossSpawnSchedule(DateTimeOffset localNow, ISimRng rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _date = DateOnly.FromDateTime(localNow.DateTime);
        Minute = _rng.NextInclusive(1, 45);
    }

    public bool TryBeginPass(DateTimeOffset localNow, TimeSpan elapsed)
    {
        var date = DateOnly.FromDateTime(localNow.DateTime);
        if (date < _date)
        {
            return false;
        }

        if (date > _date)
        {
            _date = date;
            Minute = _rng.NextInclusive(1, 45);
        }

        var slot = new DateTime(date.Year, date.Month, date.Day, localNow.Hour, Minute, 0);
        if (localNow.Minute != Minute || slot <= _lastSlot)
        {
            return false;
        }

        if (_lastPassElapsed is TimeSpan previous && elapsed - previous <= TimeSpan.FromMinutes(10))
        {
            return false;
        }

        _lastSlot = slot;
        _lastPassElapsed = elapsed;
        return true;
    }
}
