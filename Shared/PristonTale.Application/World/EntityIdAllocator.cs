namespace PristonTale.Application.World;

// Monster, summon and NPC ids, shared by every field: 64-bit and monotonic from MonsterIdBase, so an id is never reused
// and stays above every player id (players count from 1 and would need four billion logins to reach the base).
public sealed class EntityIdAllocator
{
    public const ulong MonsterIdBase = 1UL << 32;

    private long _issued;

    public ulong NextMonsterId() => MonsterIdBase + (ulong)Interlocked.Increment(ref _issued) - 1;
}
