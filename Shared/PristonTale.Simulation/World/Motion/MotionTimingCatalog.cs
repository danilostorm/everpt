using System;
using System.Collections.Generic;
using System.IO;

namespace PristonTale.Simulation.World.Motion;

public sealed class MotionTimingCatalog
{
    private readonly IReadOnlyDictionary<string, MotionTimingSet> _byModelKey;

    public MotionTimingCatalog(IReadOnlyDictionary<string, MotionTimingSet> byModelKey)
        => _byModelKey = byModelKey ?? throw new ArgumentNullException(nameof(byModelKey));

    public static MotionTimingCatalog Empty { get; } = new(new Dictionary<string, MotionTimingSet>());

    public int Count => _byModelKey.Count;

    public bool TryGet(string modelPath, out MotionTimingSet set)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            set = null;
            return false;
        }

        return _byModelKey.TryGetValue(NormalizeKey(modelPath), out set);
    }

    public static string NormalizeKey(string modelPath)
    {
        var key = Path.ChangeExtension(modelPath, ".inx");
        return key.Replace('\\', '/').ToLowerInvariant();
    }
}
