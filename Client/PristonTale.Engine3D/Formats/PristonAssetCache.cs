using System.Collections.Concurrent;

namespace PristonTale.Engine3D.Formats;

public sealed class PristonAssetCache
{
    private readonly ConcurrentDictionary<string, PristonModelInfo> _inx = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PristonModel> _models = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PristonStage> _stages = new(StringComparer.Ordinal);

    public PristonModelInfo GetOrAddModelInfo(string resolvedPath, Func<string, PristonModelInfo> parse)
    {
        var key = CacheKey(resolvedPath);
        // The key is lower-cased for case-insensitive matching, but parsing must use the
        // ORIGINAL resolvedPath so the file is read with its real (case-sensitive) name.
        return _inx.GetOrAdd(key, static (_, arg) => arg.parse(arg.resolvedPath), (resolvedPath, parse));
    }

    public PristonModel GetOrAddModel(string resolvedPath, Func<string, PristonModel> parse)
    {
        var key = CacheKey(resolvedPath);
        return _models.GetOrAdd(key, static (_, arg) => arg.parse(arg.resolvedPath), (resolvedPath, parse));
    }

    public PristonStage GetOrAddStage(string resolvedPath, Func<string, PristonStage> parse)
    {
        var key = CacheKey(resolvedPath);
        return _stages.GetOrAdd(key, static (_, arg) => arg.parse(arg.resolvedPath), (resolvedPath, parse));
    }

    public void Clear()
    {
        _inx.Clear();
        _models.Clear();
        _stages.Clear();
    }

    private static string CacheKey(string resolvedPath) => resolvedPath.ToLowerInvariant();
}
