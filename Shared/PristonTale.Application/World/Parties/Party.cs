using PristonTale.Contracts.Social;

namespace PristonTale.Application.World;

// One party: two to six players, the leader first. It changes on the tick's command phase only; fields read Members and
// AverageLevel while they step in parallel, and a change replaces the array instead of writing into it.
public sealed class Party
{
    private ulong[] _members;

    internal Party(ulong leader, ulong member) => _members = [leader, member];

    public ReadOnlySpan<ulong> Members => _members;
    public ulong LeaderId => _members[0];
    public int Count => _members.Length;
    public bool IsFull => _members.Length >= PartyLimits.MaxMembers;

    // Refreshed with View (WorldServer.RefreshParties).
    public int AverageLevel { get; internal set; }

    // What every member's party window shows; null until the first refresh.
    public PartyView? View { get; internal set; }

    public bool Contains(ulong entityId) => Array.IndexOf(_members, entityId) >= 0;

    internal void Add(ulong entityId) => _members = [.. _members, entityId];

    internal void Remove(ulong entityId) => _members = Array.FindAll(_members, member => member != entityId);

    internal void Lead(ulong entityId) => _members = [entityId, .. Array.FindAll(_members, member => member != entityId)];
}

// A member as the party window, the minimap and the name boxes show it; the leader comes first.
public readonly record struct PartyMemberView(ulong EntityId, string Name, int Level, int Life, int MaxLife, float X, float Z);

public sealed class PartyView(PartyMemberView[] members)
{
    public IReadOnlyList<PartyMemberView> Members => members;

    public bool SameAs(ReadOnlySpan<PartyMemberView> other) => members.AsSpan().SequenceEqual(other);
}

// A request to join waiting for its answer. From asks; Joiner is who joins on acceptance (From itself, or the player a
// member recommended to its leader, then named to the leader).
public sealed record PartyRequest(ulong FromId, string FromName, ulong JoinerId, string JoinerName, long ExpiresMs)
{
    public bool Recommendation => JoinerId != FromId;
}
