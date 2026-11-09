using PristonTale.Simulation.World.Ai;

namespace PristonTale.Tests;

// Returns the given values in order, whatever range is asked, then 0; NextUnit returns the value over 100.
internal sealed class ScriptedRng(params int[] values) : ISimRng
{
    private int _next;

    public int NextInclusive(int minInclusive, int maxInclusive) => Next();
    public int NextExclusive(int minInclusive, int maxExclusive) => Next();
    public float NextUnit() => Next() / 100f;

    private int Next() => _next < values.Length ? values[_next++] : 0;
}
