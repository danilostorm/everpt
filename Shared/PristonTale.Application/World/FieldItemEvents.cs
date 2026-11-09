using System.Numerics;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World;

// A visible effect of an item use around a player; Position is the player's position when it was used.
// Target is the entity a pet strikes (0 for other effects).
public readonly record struct ItemEffectView(ulong EntityId, uint ItemCode, ItemEffectKind Effect, Vector3 Position, int Variant, ulong Target = 0);

// Item effects of one field. Uses commit in the command phase, before the field steps, so they wait in a pending list
// that the next step publishes with that tick's snapshot.
internal sealed class FieldItemEvents
{
    private readonly List<ItemEffectView> _pending = new();
    private ItemEffectView[] _current = [];

    public void Add(in ItemEffectView effect) => _pending.Add(effect);

    public void BeginTick()
    {
        _current = _pending.Count == 0 ? [] : _pending.ToArray();
        _pending.Clear();
    }

    public ItemEffectView[] Snapshot() => _current;
}
