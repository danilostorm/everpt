using System.Numerics;
using PristonTale.Protocol.Packets;
using PristonTale.Contracts.Skills;

namespace PristonTale.Client.Net.Presence;

// A skill start or impact on the server timeline of the presence update that carries it.
public sealed record SkillEventInfo(
    ulong CasterId,
    ulong TargetId,
    uint SkillCode,
    SkillEventPhase Phase,
    int Rank,
    SkillEventFlags Flags,
    float DurationMs,
    float HitAtMs,
    Vector3 Position,
    float Facing,
    int TargetCount,
    float Radius);

public sealed record StatusEffectInfo(
    ulong EntityId,
    ulong SourceId,
    uint StatusCode,
    StatusEffectOp Op,
    int Stacks,
    byte SourceKind,
    byte Control,
    ushort Flags,
    int DurationMs,
    int RemainingMs,
    int Value)
{
    public bool Harmful => (Flags & StatusEffectFlags.Harmful) != 0;
    public bool Permanent => (Flags & StatusEffectFlags.Permanent) != 0;
}

public sealed record SkillCooldownInfo(uint SkillCode, int RemainingMs, int DurationMs);

// The owner's running cooldowns when the server sent them; skills absent from the list are ready.
public sealed record SkillCooldownSnapshot(double? ServerTimeMs, IReadOnlyList<SkillCooldownInfo> Cooldowns);

public sealed record SkillCastResultInfo(
    uint Sequence,
    uint SkillCode,
    SkillCastVerdict Status,
    int Rank,
    int CooldownMs,
    int RemainingMs,
    int Mana,
    int Stamina)
{
    public bool Accepted => Status == SkillCastVerdict.Accepted;
}
