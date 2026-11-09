using PristonTale.Engine3D.Formats;
using PristonTale.Contracts.Motion;

namespace PristonTale.Gameplay.World.Remote;

internal readonly record struct NpcStandMotionUpdate(bool MotionChanged, int MotionIndex);

internal sealed class NpcStandMotionCycle
{
    private const float SecondsPerAuthoredFrame = 33f / 1000f;
    private const float MaximumAdvanceSeconds = 2f;

    private readonly PristonModelInfo _info;
    private readonly int[] _standIndices;
    private readonly int[] _weightedStandIndices;
    private uint _randomState;
    private int _motionIndex = -1;
    private float _elapsedSeconds;
    private bool _active;

    public NpcStandMotionCycle(PristonModelInfo info, ulong entityId)
    {
        ArgumentNullException.ThrowIfNull(info);
        _info = info;
        _standIndices = CollectStandIndices(info);
        _weightedStandIndices = CollectWeightedStandIndices(info, _standIndices);
        _randomState = (uint)(entityId ^ entityId >> 32) ^ info.FileTypeKeyword ^ 0xA341316Cu;
        if (_randomState == 0)
        {
            _randomState = 1;
        }
    }

    public NpcStandMotionUpdate Update(float deltaSeconds, MotionCode authoritativeMotion)
    {
        if (authoritativeMotion != MotionCode.Stand || _standIndices.Length == 0)
        {
            _active = false;
            _motionIndex = -1;
            _elapsedSeconds = 0f;
            return default;
        }

        var motionChanged = false;
        if (!_active)
        {
            SelectNextMotion();
            _active = true;
            motionChanged = true;
        }

        var remaining = Math.Clamp(deltaSeconds, 0f, MaximumAdvanceSeconds);
        while (remaining > 0f)
        {
            var motion = _info.Motions[_motionIndex];
            var duration = MotionDuration(motion);
            var untilBoundary = duration - _elapsedSeconds;
            var step = MathF.Min(remaining, untilBoundary);
            var nextElapsed = _elapsedSeconds + step;
            _elapsedSeconds = nextElapsed;
            remaining -= step;
            if (_elapsedSeconds < duration)
            {
                break;
            }

            SelectNextMotion();
            motionChanged = true;
        }

        return new NpcStandMotionUpdate(motionChanged, _motionIndex);
    }

    private void SelectNextMotion()
    {
        var choices = _weightedStandIndices.Length > 0 ? _weightedStandIndices : _standIndices;
        _motionIndex = choices[NextRandom(choices.Length)];
        _elapsedSeconds = 0f;
    }

    private int NextRandom(int upperBound)
    {
        _randomState = unchecked((_randomState * 214013u) + 2531011u);
        var value = (_randomState >> 16) & 0x7FFFu;
        return (int)(value % (uint)upperBound);
    }

    private static float MotionDuration(PristonMotionInfo motion)
    {
        return (motion.EndFrame - motion.StartFrame) * SecondsPerAuthoredFrame;
    }

    private static int[] CollectStandIndices(PristonModelInfo info)
    {
        var count = Math.Min(info.Motions.Count, checked((int)info.MotionCount));
        var indices = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var motion = info.Motions[index];
            if (motion.State != (uint)MotionCode.Stand || motion.EndFrame <= motion.StartFrame)
            {
                continue;
            }

            if (motion.ItemCodeCount == 0 || motion.ItemIndices.Any(static item => item is -1 or byte.MaxValue))
            {
                indices.Add(index);
            }
        }

        return indices.ToArray();
    }

    private static int[] CollectWeightedStandIndices(PristonModelInfo info, IReadOnlyList<int> standIndices)
    {
        var weightedMode = info.NpcMotionRates.Count > PristonFormatConstants.CharacterMotionBase
            && info.NpcMotionRates[PristonFormatConstants.CharacterMotionBase] != 0;
        if (!weightedMode || info.NpcMotionSelection.Count == 0)
        {
            return [];
        }

        var allowed = new HashSet<int>(standIndices);
        var weighted = new List<int>(PristonFormatConstants.MotionRateSelectionCount);
        var count = Math.Min(info.NpcMotionSelection.Count, PristonFormatConstants.MotionRateSelectionCount);
        for (var index = 0; index < count; index++)
        {
            var selected = info.NpcMotionSelection[index];
            if (allowed.Contains(selected))
            {
                weighted.Add(selected);
            }
        }

        return weighted.ToArray();
    }

}
