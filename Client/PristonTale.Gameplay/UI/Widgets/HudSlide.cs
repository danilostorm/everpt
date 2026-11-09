using PristonTale.Contracts.Units;

namespace PristonTale.Gameplay.UI.Widgets;

// Original window slide at the original 70 Hz frame rate: opening jumps to 184 and climbs by 7 (slowing past
// 200 and 230) to 256; closing drops 16 per frame. Windows are drawn at y = 600 - move + 56.
public sealed class HudSlide
{
    public const int Full = 256;
    private const double Frame = 1.0 / GameUnits.TicksPerSecond;
    private double _accumulator;
    private bool _started;

    public bool Open { get; set; }
    public int Move { get; private set; }
    public bool Visible => Move > 0;
    public bool FullyOpen => Open && Move >= Full;
    public int Offset => Full - Math.Clamp(Move, 0, Full);

    public void Advance(double seconds)
    {
        _accumulator = Math.Min(_accumulator + Math.Max(0, seconds), Frame * 64);
        while (_accumulator >= Frame)
        {
            _accumulator -= Frame;
            Step();
        }
    }

    private void Step()
    {
        if (!Open)
        {
            Move = Math.Max(0, Move - 16);
            _started &= Move > 0;
            return;
        }

        if (!_started)
        {
            Move += 184;
            _started = true;
        }
        else if (Move < Full)
        {
            Move += 7;
            if (Move >= 200)
            {
                Move -= 2;
            }

            if (Move >= 230)
            {
                Move -= 2;
            }
        }

        Move = Math.Min(Full, Move);
    }
}
