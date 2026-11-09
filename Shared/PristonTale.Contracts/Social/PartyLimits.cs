namespace PristonTale.Contracts.Social;

// The original's party sizes, shared by its rules, its messages and the HUD: the members of one party (the leader
// first), the planar distance within which a member shares a kill's experience and gold, and the shorter one within which
// the minimap draws a member in its own colour (red beyond it).
public static class PartyLimits
{
    public const int MaxMembers = 6;
    public const float ShareRange = 18 * 64;
    public const float NearRange = 17 * 64;
}

// What a party member asks of its party, as the party window's buttons number them. The first three are the leader's.
public enum PartyCommand : byte
{
    Delegate = 0,
    Disband = 1,
    Kick = 2,
    Leave = 3
}
