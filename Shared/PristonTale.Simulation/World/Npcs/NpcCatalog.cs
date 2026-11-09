namespace PristonTale.Simulation.World.Npcs;

public sealed class NpcCatalog
{
    private readonly Dictionary<int, NpcDefinition> _byCode;

    public NpcCatalog(IEnumerable<NpcDefinition> definitions)
    {
        _byCode = definitions.ToDictionary(static d => d.Code);
        All = _byCode.Values.ToList();
    }

    public IReadOnlyList<NpcDefinition> All { get; }

    public NpcDefinition Get(int code) => _byCode[code];

    public bool TryGet(int code, out NpcDefinition definition) => _byCode.TryGetValue(code, out definition);
}
