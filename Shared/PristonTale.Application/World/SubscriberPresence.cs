using PristonTale.Simulation.World.Loot;

namespace PristonTale.Application.World;

public sealed class SubscriberPresence
{
    private readonly ulong _selfEntityId;

    private readonly Dictionary<ulong, EntityView> _spawned = new();

    // Scratch reused across ticks to mark which known ids are still present, avoiding per-tick allocs.
    private readonly HashSet<ulong> _stillPresent = new();

    public SubscriberPresence(ulong selfEntityId) => _selfEntityId = selfEntityId;

    private readonly HashSet<Guid> _knownDrops = [];
    private readonly HashSet<Guid> _visibleDrops = [];

    public PresenceDiff Diff(in WorldSnapshot snapshot)
    {
        var diff = Diff(snapshot.Entities, snapshot.Events);
        _visibleDrops.Clear();
        var added = new List<GroundDropView>();
        foreach (var drop in snapshot.GroundDrops)
        {
            if (!drop.Public && drop.OwnerId != _selfEntityId)
            {
                continue;
            }

            _visibleDrops.Add(drop.View.Id);
            if (_knownDrops.Add(drop.View.Id))
            {
                added.Add(drop.View);
            }
        }

        var removed = _knownDrops.Where(id => !_visibleDrops.Contains(id)).ToArray();
        foreach (var id in removed)
        {
            _knownDrops.Remove(id);
        }

        return new PresenceDiff(diff.Spawns, diff.States, diff.Removed, diff.Events)
        {
            GroundAdded = added,
            GroundRemoved = removed
        };
    }

    public PresenceDiff Diff(EntityView[] roster, CombatEventView[]? events = null)
    {
        var spawns = new List<EntityView>();
        var states = new List<EntityView>();
        _stillPresent.Clear();

        foreach (var view in roster)
        {
            _stillPresent.Add(view.EntityId);

            var stamped = view.EntityId == _selfEntityId ? WithSelf(view) : view;

            if (!_spawned.TryGetValue(view.EntityId, out var previous))
            {
                spawns.Add(stamped);
            }
            else if (previous.MaxLife != view.MaxLife || previous.Level != view.Level
                || previous.Appearance.WeaponKey != view.Appearance.WeaponKey
                || previous.Appearance.ArmorKey != view.Appearance.ArmorKey
                || previous.Appearance.OffHandKey != view.Appearance.OffHandKey
                || previous.Appearance.WeaponGlow != view.Appearance.WeaponGlow
                || previous.Appearance.OffHandGlow != view.Appearance.OffHandGlow
                || previous.Appearance.InVillage != view.Appearance.InVillage)
            {
                // A full descriptor refresh carries maxima and identity-level changes absent from slim states.
                spawns.Add(stamped);
            }
            else if (previous.Position != view.Position || previous.Facing != view.Facing || previous.MoveState != view.MoveState
                || previous.Life != view.Life || previous.MoveSpeed != view.MoveSpeed || previous.AttackRate != view.AttackRate
                || previous.AttackTargetId != view.AttackTargetId || previous.AttackSequence != view.AttackSequence)
            {
                states.Add(stamped);
            }

            _spawned[view.EntityId] = view;
        }

        List<ulong>? removed = null;
        foreach (var knownId in _spawned.Keys)
        {
            if (!_stillPresent.Contains(knownId))
            {
                removed ??= new List<ulong>();
                removed.Add(knownId);
            }
        }

        if (removed is not null)
        {
            foreach (var id in removed)
            {
                _spawned.Remove(id);
            }
        }

        return new PresenceDiff(
            spawns,
            states,
            removed ?? EmptyIds,
            events ?? EmptyEvents);
    }

    private static readonly IReadOnlyList<ulong> EmptyIds = Array.Empty<ulong>();
    private static readonly IReadOnlyList<CombatEventView> EmptyEvents = Array.Empty<CombatEventView>();

    private static EntityView WithSelf(EntityView v) => new(
        v.EntityId, v.Kind, isSelf: true, v.Position, v.Facing, v.MoveState,
        v.Level, v.Life, v.MaxLife, v.Appearance, v.TemplateId, v.MoveSpeed, v.AttackRate, v.AttackTargetId, v.AttackSequence);
}

public readonly struct PresenceDiff
{
    public PresenceDiff(
        IReadOnlyList<EntityView> spawns, IReadOnlyList<EntityView> states, IReadOnlyList<ulong> removed,
        IReadOnlyList<CombatEventView>? events = null)
    {
        Spawns = spawns;
        States = states;
        Removed = removed;
        Events = events ?? Array.Empty<CombatEventView>();
    }

    public IReadOnlyList<GroundDropView> GroundAdded { get; init; } = [];
    public IReadOnlyList<Guid> GroundRemoved { get; init; } = [];

    public IReadOnlyList<EntityView> Spawns { get; }
    public IReadOnlyList<EntityView> States { get; }
    public IReadOnlyList<ulong> Removed { get; }

    public IReadOnlyList<CombatEventView> Events { get; }
}
