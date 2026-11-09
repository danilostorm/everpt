using System.Collections.Immutable;
using PristonTale.Contracts.Skills;

namespace PristonTale.Application.World;

// The statuses running on the entities of one field, kept from the status events the field published, so a viewer that
// starts seeing an entity (it walked into range, joined, or the entity arrived from another field) receives the statuses
// that began before. The tick mutates it; snapshots carry the immutable view of the moment.
internal sealed class FieldStatusBook
{
    // Pruning of expired timers and departed entities; lookups also skip expired entries.
    private const long PruneIntervalMs = 5000;
    private ImmutableDictionary<ulong, ImmutableArray<ActiveStatusView>> _entries = ImmutableDictionary<ulong, ImmutableArray<ActiveStatusView>>.Empty;
    private long _nextPruneMs = PruneIntervalMs;

    public ActiveStatusSet Snapshot => new(_entries);

    public void Apply(IReadOnlyList<StatusEventView> events, long nowMs)
    {
        for (var index = 0; index < events.Count; index++)
        {
            var status = events[index];
            if (status.Op == StatusEffectOp.Tick)
            {
                continue;
            }

            var current = _entries.TryGetValue(status.EntityId, out var list) ? list : ImmutableArray<ActiveStatusView>.Empty;
            var position = IndexOf(current, status);
            if (status.Op == StatusEffectOp.Remove)
            {
                if (position >= 0)
                {
                    current = current.RemoveAt(position);
                    _entries = current.IsEmpty ? _entries.Remove(status.EntityId) : _entries.SetItem(status.EntityId, current);
                }

                continue;
            }

            var entry = new ActiveStatusView(status with { Op = StatusEffectOp.Add }, nowMs);
            current = position >= 0 ? current.SetItem(position, entry) : current.Add(entry);
            _entries = _entries.SetItem(status.EntityId, current);
        }
    }

    // The statuses of an entity leaving the field; a field change carries them to the destination.
    public ImmutableArray<ActiveStatusView> Take(ulong entityId)
    {
        if (!_entries.TryGetValue(entityId, out var list))
        {
            return ImmutableArray<ActiveStatusView>.Empty;
        }

        _entries = _entries.Remove(entityId);
        return list;
    }

    // Statuses carried from another field, rebased from its clock to this one.
    public void Carry(ulong entityId, ImmutableArray<ActiveStatusView> statuses, long fromNowMs, long toNowMs)
    {
        if (statuses.IsEmpty)
        {
            return;
        }

        var builder = ImmutableArray.CreateBuilder<ActiveStatusView>(statuses.Length);
        foreach (var status in statuses)
        {
            builder.Add(status with { AtMs = status.AtMs - fromNowMs + toNowMs });
        }

        _entries = _entries.SetItem(entityId, builder.MoveToImmutable());
    }

    public void Prune(long nowMs, Func<ulong, bool> present)
    {
        if (nowMs < _nextPruneMs || _entries.IsEmpty)
        {
            return;
        }

        _nextPruneMs = nowMs + PruneIntervalMs;
        var pruned = _entries.ToBuilder();
        foreach (var (entity, list) in _entries)
        {
            if (!present(entity))
            {
                pruned.Remove(entity);
                continue;
            }

            var kept = list.RemoveAll(status => status.RemainingAt(nowMs) <= 0);
            if (kept.Length != list.Length)
            {
                if (kept.IsEmpty)
                {
                    pruned.Remove(entity);
                }
                else
                {
                    pruned[entity] = kept;
                }
            }
        }

        _entries = pruned.ToImmutable();
    }

    // The client matches statuses the same way: a harmful status per source, any other one per code.
    private static int IndexOf(ImmutableArray<ActiveStatusView> list, in StatusEventView status)
    {
        for (var index = 0; index < list.Length; index++)
        {
            var existing = list[index].Status;
            if (existing.StatusCode == status.StatusCode && (!existing.Harmful || existing.SourceId == status.SourceId))
            {
                return index;
            }
        }

        return -1;
    }
}

// A status as last published, with the field time it was published at.
public readonly record struct ActiveStatusView(StatusEventView Status, long AtMs)
{
    // Remaining time at a later field time; permanent statuses never run out.
    public int RemainingAt(long nowMs) => Status.Permanent ? int.MaxValue : (int)Math.Max(0, Status.RemainingMs - (nowMs - AtMs));

    public StatusEventView At(long nowMs) => Status.Permanent ? Status : Status with { RemainingMs = RemainingAt(nowMs) };
}

public readonly struct ActiveStatusSet
{
    private readonly ImmutableDictionary<ulong, ImmutableArray<ActiveStatusView>>? _entries;

    public ActiveStatusSet(ImmutableDictionary<ulong, ImmutableArray<ActiveStatusView>> entries) => _entries = entries;

    public ImmutableArray<ActiveStatusView> For(ulong entityId) =>
        _entries is not null && _entries.TryGetValue(entityId, out var list) ? list : ImmutableArray<ActiveStatusView>.Empty;
}
