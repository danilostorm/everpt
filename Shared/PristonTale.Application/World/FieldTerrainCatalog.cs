using PristonTale.Simulation.World;

namespace PristonTale.Application.World;

// Stage collision per field code, filled once at startup before players join. Fields without a loaded stage use
// FieldTerrain.None and keep the heights they are given.
public sealed class FieldTerrainCatalog
{
    private volatile IReadOnlyDictionary<int, FieldTerrain> _terrain = new Dictionary<int, FieldTerrain>();

    public int Count => _terrain.Count;

    public FieldTerrain Get(int fieldCode) => _terrain.TryGetValue(fieldCode, out var terrain) ? terrain : FieldTerrain.None;

    public void Publish(IReadOnlyDictionary<int, FieldTerrain> terrain) => _terrain = terrain;
}
