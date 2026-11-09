namespace PristonTale.Gameplay.World;

public enum TargetLiveness
{
    Missing,
    Alive,
    Dead
}

public delegate TargetLiveness TargetLivenessQuery(ulong entityId);

// Which entity the target frame shows, following the original client: the traced target (a clicked monster or NPC)
// always owns the frame; without one, the entity under the cursor replaces it. The frame stays after the swing and
// after a ground click (cancelling an attack clears only the trace) and closes when the entity is removed or has been
// dead for 0x50 frames at 70 Hz. The original's 7 s display timeout maps to removal here:
// the server sends RemoveObjects when an entity leaves view and sends nothing for idle ones.
public sealed class TargetSelection
{
    public const double DeathCloseDelayMs = 0x50 * 1000d / 70d;

    private double _deadSinceMs = double.NaN;

    public ulong TracedId { get; private set; }

    public ulong HoveredId { get; private set; }

    public ulong FrameId { get; private set; }

    public void Trace(ulong entityId)
    {
        TracedId = entityId;
    }

    public void ClearTrace()
    {
        TracedId = 0;
    }

    public void Hover(ulong entityId)
    {
        HoveredId = entityId;
    }

    public void Clear()
    {
        TracedId = 0;
        HoveredId = 0;
        Show(0);
    }

    public void Update(double nowMs, TargetLivenessQuery query)
    {
        if (TracedId != 0 && query(TracedId) != TargetLiveness.Alive)
        {
            TracedId = 0;
        }

        if (TracedId != 0)
        {
            Show(TracedId);
        }
        else if (HoveredId != 0 && HoveredId != FrameId && query(HoveredId) == TargetLiveness.Alive)
        {
            Show(HoveredId);
        }

        if (FrameId == 0)
        {
            return;
        }

        switch (query(FrameId))
        {
            case TargetLiveness.Missing:
                Show(0);
                break;
            case TargetLiveness.Dead:
                if (double.IsNaN(_deadSinceMs))
                {
                    _deadSinceMs = nowMs;
                }
                else if (nowMs - _deadSinceMs > DeathCloseDelayMs)
                {
                    Show(0);
                }

                break;
            default:
                _deadSinceMs = double.NaN;
                break;
        }
    }

    private void Show(ulong entityId)
    {
        if (FrameId != entityId)
        {
            FrameId = entityId;
            _deadSinceMs = double.NaN;
        }
    }
}
