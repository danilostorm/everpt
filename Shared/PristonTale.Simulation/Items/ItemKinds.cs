namespace PristonTale.Simulation.Items;

// The kind of an item by its catalog key, which is the only place the data carries it: wearables by their equipment family
// (ItemEquipmentMetadata), the rest by the first two characters. The importer stores it; the runtime reads the same table.
public static class ItemKinds
{
    private static readonly Dictionary<string, ItemKind> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FO"] = ItemKind.ForceOrb,
        ["PM"] = ItemKind.Potion,
        ["PL"] = ItemKind.Potion,
        ["PS"] = ItemKind.Potion,
        ["QT"] = ItemKind.Quest,
        ["MA"] = ItemKind.Material
    };

    public static ItemKind Classify(string catalogKey)
    {
        if (ItemEquipmentMetadata.TryGet(catalogKey, out var kind, out _))
        {
            return kind;
        }

        return catalogKey is { Length: >= 2 } && Groups.TryGetValue(catalogKey[..2], out kind) ? kind : ItemKind.Misc;
    }
}
