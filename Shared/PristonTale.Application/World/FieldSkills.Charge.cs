using PristonTale.Simulation.Skills;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// Held charges: the client reports the release of the button; a release before the full frame jumps the motion ahead
// (from the window start at the earliest) and leaves the charge partial. The server keeps the timeline: holding past
// the full frame charges fully even if the release report arrives later.
internal sealed partial class FieldSkills
{
    private SkillCastOutcome Release(WorldPlayer player, in SkillCastRequest request)
    {
        if (!_casts.TryGetValue(player.EntityId, out var cast) || cast.Charge is not { } charge || cast.Rule.Code != request.SkillCode
            || cast.Released || !player.IsCasting)
        {
            return SkillCastOutcome.Refused(request, SkillCastVerdict.Rejected);
        }

        cast.Released = true;
        var elapsed = player.CastElapsedMs;
        if (elapsed >= charge.FullMs)
        {
            cast.Full = true;
            return new SkillCastOutcome(request.Sequence, request.SkillCode, SkillCastVerdict.Accepted, cast.Rank, 0, 0, player.Character.Mana,
                player.Character.Stamina);
        }

        var shift = charge.FullMs - MathF.Max(elapsed, charge.StartMs);
        for (var index = 0; index < cast.HitsAtMs.Length; index++)
        {
            cast.HitsAtMs[index] -= shift;
        }

        var remaining = charge.FullDurationMs - shift - elapsed;
        player.ShortenCast(charge.FullDurationMs - shift);
        var nextHit = cast.HitsAtMs.Length > 0 ? MathF.Max(0f, cast.HitsAtMs[0] - elapsed) : remaining;
        _pendingStarts.Add(new SkillEventView(player.EntityId, cast.TargetId, cast.Rule.Code, SkillEventPhase.Release, cast.Rank,
            SkillEventFlags.None, MathF.Max(0f, remaining), nextHit, cast.Aim, player.Facing, 0, 0f));
        return new SkillCastOutcome(request.Sequence, request.SkillCode, SkillCastVerdict.Accepted, cast.Rank, 0, 0, player.Character.Mana,
            player.Character.Stamina);
    }
}
