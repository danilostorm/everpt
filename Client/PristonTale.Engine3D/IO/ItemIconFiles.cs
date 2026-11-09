using PristonTale.Engine3D.Rendering;
using PristonTale.Assets;

namespace PristonTale.Engine3D.IO;

// Inventory sprites of items: <assetRoot>/image/Sinimage/Items/**/it<KEY>.bmp, one 22-pixel square per inventory cell.
public static class ItemIconFiles
{
    public const int CellPixels = 22;

    // Catalog key to sprite path. DropItem holds model texture sheets whose names collide with the sprites.
    public static Dictionary<string, string> Index(string assetRoot)
    {
        var root = AssetFiles.Resolve(Path.Combine(assetRoot, "image", "Sinimage", "Items"));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var relative = Path.GetRelativePath(root, Path.GetDirectoryName(path));
            if (!Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase)
                || relative.Split(Path.DirectorySeparatorChar).Any(part => part.Equals("DropItem", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (name.StartsWith("it", StringComparison.OrdinalIgnoreCase) && name.Length > 2)
            {
                result.TryAdd(name[2..], path);
            }
        }

        return result;
    }

    // The inventory cells a sprite covers; a partial cell is the sprite's margin and does not count.
    public static bool TryFootprint(string path, out int width, out int height)
    {
        var image = PristonImageDecoder.Decode(AssetFiles.ReadAllBytes(path), ".bmp");
        width = Math.Max(1, image.Width / CellPixels);
        height = Math.Max(1, image.Height / CellPixels);
        return image.IsValid;
    }
}
