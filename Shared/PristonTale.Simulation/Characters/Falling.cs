using PristonTale.Contracts.Units;

namespace PristonTale.Simulation.Characters;

// The original's fall: a player more than 8 units above its floor drops 8 units a frame at 70 frames a second, and
// stands on the floor once within 8 units of it.
public static class Falling
{
    public const float SnapDistance = 8f;
    public const float UnitsPerSecond = 8f * GameUnits.TicksPerSecond;

    public static bool IsAirborne(float height, float floor) => height - floor > SnapDistance;

    // The height after the given seconds of falling, never below the floor.
    public static float Step(float height, float floor, float seconds) => MathF.Max(floor, height - (UnitsPerSecond * seconds));
}
