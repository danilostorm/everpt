namespace PristonTale.Simulation.Items;

// Absorption added to the authored absorb of armor, robes, boots, gloves and shields by catalog number, as the
// English client rates them. Index 0 is number 01.
public static class ItemAbsorbCorrection
{
    private static readonly Dictionary<string, float[]> ByFamily = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DA1"] = [0f, 0.3f, 0.6f, 1.0f, 1.3f, 1.6f, 1.9f, 2.3f, 2.6f, 3.0f, 3.4f, 3.7f, 4.0f, 4.2f, 4.5f, 4.8f, 5.3f, 6.0f, 7.0f, 8.4f],
        ["DA2"] = [0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f, 1.8f, 2.1f, 2.4f, 2.7f, 3.0f, 3.3f, 3.6f, 3.9f, 4.2f, 4.5f, 4.9f, 5.5f, 6.8f, 8.0f],
        ["DB1"] = [0f, 0f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 1.1f, 1.3f, 1.5f, 1.7f, 1.9f, 2.1f, 2.3f, 2.5f, 2.7f, 2.9f, 3.1f],
        ["DG1"] = [0f, 0f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f, 1.7f, 1.9f, 2.1f, 2.3f, 2.5f, 2.7f, 2.9f],
        ["DS1"] = [0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f, 1.0f, 1.1f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2.0f, 2.1f, 2.2f, 2.3f, 2.4f]
    };

    public static float For(string catalogKey)
    {
        if (catalogKey is null || catalogKey.Length != 5 || !ByFamily.TryGetValue(catalogKey[..3], out var table)
            || !int.TryParse(catalogKey.AsSpan(3), out var number) || number < 1 || number > table.Length)
        {
            return 0f;
        }

        return table[number - 1];
    }

    // The absorb an item copy rates with: its own absorb plus the catalog correction.
    public static float Rated(ItemInstance item) => item.Roll.Absorb + For(item.Definition.CatalogKey);
}
