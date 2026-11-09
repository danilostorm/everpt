using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

// The field's weather: the world's while the field takes rain (WeatherSchedule.Rains), clear otherwise; every snapshot
// carries it and the encoder sends it when it changes.
public sealed partial class FieldInstance
{
    // Set by the world when it opens the field.
    internal bool Rains { get; set; }

    public WeatherState Weather { get; private set; }

    // Set by the world before each step.
    internal void SetWeather(WeatherState world) => Weather = Rains ? world : WeatherState.Clear;
}
