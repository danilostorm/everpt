namespace PristonTale.Application.World;

// The kinds of events a tick publishes; an index names one event of one kind.
public enum EventRouteKind : byte
{
    Combat,
    Skill,
    Status,
    Item
}

// The events of one field tick indexed by the entities they name (attacker and target, caster and target, the entity of a
// status or item effect). An edge serving one receiver collects the events of the entities that receiver sees instead of
// scanning every event of the field; the index is built once per tick on the field's frame and reused across ticks.
public sealed class EventRoutes
{
    // (entity id << 64 | kind << 32 | event index), sorted: the routes of one entity are one contiguous run.
    private UInt128[] _routes = [];
    private int _count;
    private object? _combat;
    private object? _skills;
    private object? _statuses;
    private object? _items;

    // The number of events indexed.
    public int EventCount { get; private set; }

    // Field tick only, while no encoder holds the frame.
    internal void Rebuild(CombatEventView[] combat, SkillEventView[] skills, StatusEventView[] statuses, ItemEffectView[] items)
    {
        _combat = combat;
        _skills = skills;
        _statuses = statuses;
        _items = items;
        EventCount = combat.Length + skills.Length + statuses.Length + items.Length;
        _count = 0;
        EnsureCapacity(combat.Length * 2 + skills.Length * 2 + statuses.Length + items.Length);
        for (var index = 0; index < combat.Length; index++)
        {
            AddPair(combat[index].AttackerId, combat[index].TargetId, EventRouteKind.Combat, index);
        }

        for (var index = 0; index < skills.Length; index++)
        {
            AddPair(skills[index].CasterId, skills[index].TargetId, EventRouteKind.Skill, index);
        }

        for (var index = 0; index < statuses.Length; index++)
        {
            Add(statuses[index].EntityId, EventRouteKind.Status, index);
        }

        for (var index = 0; index < items.Length; index++)
        {
            Add(items[index].EntityId, EventRouteKind.Item, index);
        }

        Array.Sort(_routes, 0, _count);
    }

    // Whether these are the arrays the index was built from (a receiver that fell behind gets merged copies instead).
    public bool Indexes(CombatEventView[] combat, SkillEventView[] skills, StatusEventView[] statuses, ItemEffectView[] items) =>
        ReferenceEquals(combat, _combat) && ReferenceEquals(skills, _skills) && ReferenceEquals(statuses, _statuses) && ReferenceEquals(items, _items);

    // Appends (kind << 32 | index) of every event naming the entity.
    public void Collect(ulong entityId, List<long> results)
    {
        var first = (UInt128)entityId << 64;
        var position = Array.BinarySearch(_routes, 0, _count, first);
        position = position < 0 ? ~position : position;
        while (position < _count && (ulong)(_routes[position] >> 64) == entityId)
        {
            results.Add((long)(ulong)_routes[position]);
            position++;
        }
    }

    private void AddPair(ulong first, ulong second, EventRouteKind kind, int index)
    {
        Add(first, kind, index);
        if (second != first)
        {
            Add(second, kind, index);
        }
    }

    private void Add(ulong entityId, EventRouteKind kind, int index)
    {
        if (entityId != 0)
        {
            _routes[_count++] = (UInt128)entityId << 64 | (UInt128)((ulong)kind << 32 | (uint)index);
        }
    }

    private void EnsureCapacity(int required)
    {
        if (_routes.Length < required)
        {
            _routes = new UInt128[Math.Max(required, _routes.Length * 2)];
        }
    }
}
