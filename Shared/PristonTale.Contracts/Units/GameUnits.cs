namespace PristonTale.Contracts.Units;

// The units the game data and its timing rules are authored in. Positions and scales are 8.8 fixed point; angles
// are 4096 per turn; behaviour runs at 70 ticks per second; an animation frame is 160 ticks of the clip timeline and
// plays for 33 ms at rate 1.
public static class GameUnits
{
    public const int FixedShift = 8;
    public const int FixedOne = 1 << FixedShift;
    public const float FixedScale = FixedOne;

    public const int AngleTurn = 4096;
    public const int AngleMask = AngleTurn - 1;
    public const float RadiansPerAngle = MathF.Tau / AngleTurn;

    public const int TicksPerSecond = 70;
    public const float TickSeconds = 1f / TicksPerSecond;
    public const float TickMs = 1000f / TicksPerSecond;

    public const int FrameTicks = 160;
    public const int FrameMs = 33;
    public const float FrameTicksPerSecond = FrameTicks * 1000f / FrameMs;

    public static int ToFixed(float value) => (int)MathF.Round(value * FixedScale);

    public static float FromFixed(int value) => value / FixedScale;

    // Wraps into [0, 4096).
    public static int ToAngle(float radians) => (int)MathF.Round(radians / RadiansPerAngle) & AngleMask;

    public static float FromAngle(int angle) => (angle & AngleMask) * RadiansPerAngle;

    public static float TicksToMs(float ticks) => ticks * TickMs;
}
