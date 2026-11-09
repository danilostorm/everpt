using PristonTale.Engine3D.IO;

namespace PristonTale.WorldImport;

// Inventory footprint of every item: the cells its inventory sprite covers. An item without a sprite takes the most common
// footprint of its family (the first three characters of its key), and one cell when its family has no sprite either.
public static class ItemFootprints
{
    public static Dictionary<string, (int Width, int Height)> Measure(string? assetRoot, IEnumerable<string> keys, ImportReport report)
    {
        var result = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        var all = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (assetRoot is null)
        {
            report.Warnings.Add("item footprints: no assets root (pass --assets <dir>); every item takes one inventory cell");
            return result;
        }

        var sprites = ItemIconFiles.Index(assetRoot);
        var missing = new List<string>();
        foreach (var key in all)
        {
            if (sprites.TryGetValue(key, out var path) && ItemIconFiles.TryFootprint(path, out var width, out var height))
            {
                result[key] = (width, height);
            }
            else
            {
                missing.Add(key);
            }
        }

        var familySizes = result
            .GroupBy(static entry => Family(entry.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static family => family.Key,
                static family => family.GroupBy(static entry => entry.Value).OrderByDescending(static size => size.Count())
                    .ThenBy(static size => size.Key.Width * size.Key.Height).First().Key,
                StringComparer.OrdinalIgnoreCase);
        foreach (var key in missing)
        {
            result[key] = familySizes.TryGetValue(Family(key), out var size) ? size : (1, 1);
        }

        if (missing.Count > 0)
        {
            report.Warnings.Add($"item footprints: {missing.Count} items have no inventory sprite and take their family's size: "
                + string.Join(", ", missing.Order(StringComparer.OrdinalIgnoreCase)));
        }

        return result;
    }

    private static string Family(string key) => key.Length >= 3 ? key[..3].ToUpperInvariant() : key.ToUpperInvariant();
}
