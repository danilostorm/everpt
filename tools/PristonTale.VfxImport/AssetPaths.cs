namespace PristonTale.VfxImport;

// Resolves the original's asset names (backslashes, any letter case, models named ".ase" that ship as ".smd") to
// asset-relative paths with the casing found on disk. Paths are case-insensitive only through this index.
internal sealed class AssetPaths
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public AssetPaths(string assetRoot, params string[] folders)
    {
        Root = assetRoot;
        foreach (var folder in folders)
        {
            var directory = Path.Combine(assetRoot, folder);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(assetRoot, file).Replace('\\', '/');
                _files.TryAdd(relative, relative);
            }
        }
    }

    // An index over explicit asset-relative files (tests).
    public AssetPaths(IEnumerable<string> files)
    {
        Root = "";
        foreach (var file in files)
        {
            var relative = file.Replace('\\', '/');
            _files.TryAdd(relative, relative);
        }
    }

    public string Root { get; }

    public bool TryResolve(string path, out string resolved)
    {
        var normalized = Normalize(path);
        return _files.TryGetValue(normalized, out resolved!);
    }

    // A model: the named file, or the ".smd" beside a named ".ase".
    public bool TryResolveModel(string path, out string resolved)
    {
        if (TryResolve(path, out resolved))
        {
            return !resolved.EndsWith(".ase", StringComparison.OrdinalIgnoreCase) || TryResolve(Path.ChangeExtension(resolved, ".smd"), out resolved);
        }

        return TryResolve(Path.ChangeExtension(Normalize(path), ".smd"), out resolved);
    }

    public static string Normalize(string path)
    {
        var slashed = path.Replace('\\', '/');
        while (slashed.Contains("//", StringComparison.Ordinal))
        {
            slashed = slashed.Replace("//", "/", StringComparison.Ordinal);
        }

        return slashed.TrimStart('/');
    }
}
