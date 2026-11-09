using System.Numerics;
using PristonTale.Contracts.Skills;

namespace PristonTale.Simulation.Skills;

// One cast request as the client aimed it: a target entity, a ground point or both. The server re-derives every
// value it trusts (rank, cost, range, facing).
public readonly record struct SkillCastRequest(uint Sequence, uint SkillCode, ulong TargetId, Vector3 Point, bool HasPoint,
    SkillCastCommand Command = SkillCastCommand.Cast);

public readonly record struct SkillCastOutcome(uint Sequence, uint SkillCode, SkillCastVerdict Verdict, int Rank, int CooldownMs, int RemainingMs,
    int Mana, int Stamina)
{
    public bool Accepted => Verdict == SkillCastVerdict.Accepted;

    public static SkillCastOutcome Refused(in SkillCastRequest request, SkillCastVerdict verdict, int remainingMs = 0) =>
        new(request.Sequence, request.SkillCode, verdict, 0, 0, remainingMs, 0, 0);
}
