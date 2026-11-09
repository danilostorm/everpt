namespace PristonTale.Protocol.Binary;

// An angle on the wire: a turn divided into a power-of-two number of steps.
public static class WireAngle
{
    public static int ToSteps(float radians, int steps)
    {
        var turn = radians / (2f * MathF.PI);
        return (int)MathF.Round((turn - MathF.Floor(turn)) * steps) & (steps - 1);
    }

    public static float FromSteps(int value, int steps) => value * (2f * MathF.PI / steps);
}
