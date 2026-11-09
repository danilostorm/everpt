namespace PristonTale.Simulation.World.Npcs;

public sealed record NpcShopStock(
    IReadOnlyList<uint> WeaponItemCodes,
    IReadOnlyList<uint> ArmorItemCodes,
    IReadOnlyList<uint> MiscItemCodes)
{
    public static readonly NpcShopStock Empty = new([], [], []);

    public bool IsEmpty => WeaponItemCodes.Count == 0 && ArmorItemCodes.Count == 0 && MiscItemCodes.Count == 0;

    public IReadOnlyList<uint> Tab(int tab) => tab switch
    {
        0 => WeaponItemCodes,
        1 => ArmorItemCodes,
        2 => MiscItemCodes,
        _ => []
    };

    public bool Contains(uint code) => WeaponItemCodes.Contains(code) || ArmorItemCodes.Contains(code) || MiscItemCodes.Contains(code);
}

// Name is the NPC's unique English key (the importer's identity registry); DisplayName is the English label shown to players.
public sealed record NpcDefinition(
    int Code,
    string Name,
    string ModelInxPath,
    IReadOnlyList<NpcService> Services)
{
    public NpcServiceSet ServiceSet { get; } = NpcServiceSet.From(Services);

    public string DisplayName { get; init; } = "";

    public string Label => DisplayName.Length > 0 ? DisplayName : Name;

    public NpcShopStock Shop { get; init; } = NpcShopStock.Empty;
    public IReadOnlyList<string> Dialog { get; init; } = [];

    public float ModelScale { get; init; } = 1f;

    public int SoundCode { get; init; }

    public float MoveRange { get; init; } = 64f;

    // A hostile character placed as an NPC, its level and shadow size (-1 none).
    public bool Hostile { get; init; }
    public int Level { get; init; }
    public int ShadowSizeLevel { get; init; }

    // The NPC appears only when the field's appearance draw modulo AppearCycle equals AppearSlot; a cycle of 0 always.
    public int AppearSlot { get; init; }
    public int AppearCycle { get; init; }

    public bool AppearsFor(int draw) => AppearCycle <= 0 || draw % AppearCycle == AppearSlot;

    // Authored correction of the target-frame portrait camera: height and distance in world units.
    public int PortraitOffsetY { get; init; }

    public int PortraitOffsetZ { get; init; }

    public bool Has(NpcServiceKind kind) => ServiceSet.Has(kind);

    public NpcService Find(NpcServiceKind kind)
    {
        foreach (var service in Services)
        {
            if (service.Kind == kind)
            {
                return service;
            }
        }

        return null;
    }
}
