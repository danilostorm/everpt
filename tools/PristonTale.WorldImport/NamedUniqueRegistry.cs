namespace PristonTale.WorldImport;

// The number of every named unique (Identities/named-uniques.json), by its item code and English name. A saved copy
// names its unique by that number (ItemDefinition.NamedUniqueBase + number), so a number is never reused or changed: a
// named unique missing from the registry takes the next number of its code, and the import report lists the entry to
// add to the file.
public sealed class NamedUniqueRegistry
{
    public const string FileName = "named-uniques.json";

    public sealed record Entry(string Code, int Number, string Name);

    private readonly Dictionary<(string Code, string Name), Entry> _byName = new(Comparer.Instance);
    private readonly Dictionary<string, int> _lastNumber = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Entry> _met = [];

    public NamedUniqueRegistry(IEnumerable<Entry> entries)
    {
        var numbers = new HashSet<(string, int)>();
        foreach (var entry in entries)
        {
            if (entry.Number <= 0 || entry.Name.Length == 0 || !numbers.Add((entry.Code.ToUpperInvariant(), entry.Number))
                || !_byName.TryAdd((entry.Code, entry.Name), entry))
            {
                throw new InvalidDataException($"{FileName}: {entry.Code} {entry.Number} '{entry.Name}' has no number or name, or repeats one.");
            }

            _lastNumber[entry.Code] = Math.Max(_lastNumber.GetValueOrDefault(entry.Code), entry.Number);
        }
    }

    // Named uniques this import met that the registry file lacks, in the order met.
    public List<Entry> Added { get; } = [];

    // Registered named uniques this import did not meet: saved copies of them no longer load.
    public IEnumerable<Entry> Unmet => _byName.Values.Where(entry => !_met.Contains(entry) && !Added.Contains(entry));

    public static NamedUniqueRegistry Load() => new(IdentityRegistry.ReadEntries<Entry>(FileName));

    public int Resolve(string code, string name)
    {
        if (_byName.TryGetValue((code, name), out var known))
        {
            _met.Add(known);
            return known.Number;
        }

        var number = _lastNumber.GetValueOrDefault(code) + 1;
        _lastNumber[code] = number;
        var entry = new Entry(code, number, name);
        _byName[(code, name)] = entry;
        Added.Add(entry);
        return number;
    }

    // Item codes compare case-insensitively, as references to them do; names exactly.
    private sealed class Comparer : IEqualityComparer<(string Code, string Name)>
    {
        public static readonly Comparer Instance = new();

        public bool Equals((string Code, string Name) x, (string Code, string Name) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Code, y.Code) && StringComparer.Ordinal.Equals(x.Name, y.Name);

        public int GetHashCode((string Code, string Name) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Code), StringComparer.Ordinal.GetHashCode(obj.Name));
    }
}
