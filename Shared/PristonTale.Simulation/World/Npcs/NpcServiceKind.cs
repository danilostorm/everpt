namespace PristonTale.Simulation.World.Npcs;

// Persisted in npc_services.kind, carried in the NPC dialog message and as bit (1 << kind) of the PlayerInfo service
// word; append new kinds and never renumber. A kind's Value is the integer argument of its authored key, if any.
public enum NpcServiceKind : byte
{
    Shop = 1,

    // Value: class-change group of the master (0 when absent): 1 offers only the first rank-up quest, 2 and above all three.
    SkillMaster = 2,
    Warehouse = 3,

    // Value: quest trigger code tested by the client on click (1000 and above).
    QuestGiver = 4,

    // Value, Value2: server quest code and parameter.
    QuestScript = 5,

    // Value: event quest number (default 2).
    QuestEvent = 6,

    // Value: warp gate map mode (default 1); modes 0 and 1 run the wing exchange of the wing quest.
    WarpGate = 7,

    // Value: teleport route (default 1).
    Teleporter = 8,
    ItemAging = 9,

    // Value: 1 = sheltom mixing, 200 = force orb alchemy.
    ItemMix = 10,
    Smelting = 11,
    Manufacture = 12,
    MixtureReset = 13,
    Clan = 14,

    // Value: castle role (default 1).
    BlessCastle = 15,

    // Value: event kind (default 1; 5 = Bellatra ticket booth).
    EventBooth = 16,
    EventGift = 17,
    GiftExpress = 18,

    // Value: minimum character level (default 20).
    StarPoint = 19,
    CollectMoney = 20,
    GiveMoney = 21,

    // Value: survey number (default 1).
    Polling = 22,

    // Text: stream path, a line feed, then its title.
    Media = 23,

    // Value, Value2: event code and event information of an event creature.
    EventMonster = 24,

    // Text: label of a key present in the supplied data that no classic client handles.
    Unported = 25
}

public sealed record NpcService(NpcServiceKind Kind, int Value = 0, int Value2 = 0, string Text = "");

public readonly record struct NpcServiceSet(uint Bits)
{
    public static readonly NpcServiceSet None = new(0);

    public bool IsDialogOnly => Bits == 0;

    public bool Has(NpcServiceKind kind) => (Bits & Bit(kind)) != 0;

    public NpcServiceSet With(NpcServiceKind kind) => new(Bits | Bit(kind));

    public IEnumerable<NpcServiceKind> Kinds()
    {
        foreach (var kind in Enum.GetValues<NpcServiceKind>())
        {
            if (Has(kind))
            {
                yield return kind;
            }
        }
    }

    public static NpcServiceSet From(IEnumerable<NpcService> services)
    {
        var set = None;
        foreach (var service in services)
        {
            set = set.With(service.Kind);
        }

        return set;
    }

    private static uint Bit(NpcServiceKind kind) => (byte)kind is > 0 and < 32 ? 1u << (byte)kind : 0u;
}
