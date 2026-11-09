using PristonTale.Contracts.Units;
namespace PristonTale.Simulation.World.Motion;

public readonly record struct MotionClip(
    int StartFrame,
    int EndFrame,
    uint Event0,
    uint Event1,
    uint Event2,
    uint Event3,
    bool Repeat)
{
    // The clip's position in the model's motion table (what the client plays) and its authored key character, which
    // selects the skill some monsters perform with the clip.
    public int Slot { get; init; } = -1;

    public char Key { get; init; }

    public int Frames => EndFrame - StartFrame;

    public int SpanTicks => Frames * GameUnits.FrameTicks;

    public bool TryGetEventTicks(int index, out uint ticks)
    {
        ticks = index switch
        {
            0 => Event0,
            1 => Event1,
            2 => Event2,
            3 => Event3,
            _ => 0,
        };

        if (ticks == 0 || ticks > (uint)SpanTicks)
        {
            ticks = 0;
            return false;
        }

        return true;
    }
}
