namespace PristonTale.Contracts.Social;

// A character's two lists of other players' names, as the party window's friends page keeps them. Carried on the wire
// and stored; append values only.
public enum ContactList : byte
{
    Friends = 0,
    Ignored = 1
}

// The original friends page: each list holds at most MaxEntries names, shown PageRows at a time; the recent list keeps
// the last RecentCount players whispered, traded with or befriended.
public static class ContactLimits
{
    public const int MaxEntries = 100;
    public const int PageRows = 7;
    public const int RecentCount = 7;
}
