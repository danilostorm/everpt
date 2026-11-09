using System.Collections;

namespace PristonTale.Simulation.Characters;

// An operation a character applied, keyed by the client's operation id. Applied records implement it explicitly so the
// stored JSON keeps only their own members.
public interface IAppliedOperation
{
    Guid OperationId { get; }
}

// The last operations of one kind a character applied, oldest first, kept to answer a retried operation id with its
// first result. At capacity the oldest is dropped.
public sealed class OperationHistory<TApplied> : IReadOnlyList<TApplied> where TApplied : class, IAppliedOperation
{
    public const int Capacity = 64;

    private readonly List<TApplied> _applied = new();

    public int Count => _applied.Count;

    public TApplied this[int index] => _applied[index];

    public TApplied Find(Guid operationId)
    {
        foreach (var applied in _applied)
        {
            if (applied.OperationId == operationId)
            {
                return applied;
            }
        }

        return null;
    }

    public void Record(TApplied applied)
    {
        if (_applied.Count == Capacity)
        {
            _applied.RemoveAt(0);
        }

        _applied.Add(applied);
    }

    // A stored history: at most Capacity entries, each with an id of its own that valid accepts.
    public void Restore(IEnumerable<TApplied> applied, Func<TApplied, bool> valid, string kind)
    {
        _applied.Clear();
        foreach (var entry in applied)
        {
            if (_applied.Count >= Capacity || entry is null || entry.OperationId == Guid.Empty || !valid(entry)
                || Find(entry.OperationId) is not null)
            {
                _applied.Clear();
                throw new ArgumentException($"Invalid {kind} history.");
            }

            _applied.Add(entry);
        }
    }

    public void CopyFrom(OperationHistory<TApplied> other)
    {
        _applied.Clear();
        _applied.AddRange(other._applied);
    }

    public List<TApplied>.Enumerator GetEnumerator() => _applied.GetEnumerator();

    IEnumerator<TApplied> IEnumerable<TApplied>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
