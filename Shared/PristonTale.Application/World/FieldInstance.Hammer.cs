using PristonTale.Simulation.Items.Usable;
using PristonTale.Simulation.World.Monsters;
using PristonTale.Simulation.World.Spawning;

namespace PristonTale.Application.World;

// The hammer event: a hammer-class monster that lands ten blows in a row on a player without a big head gives the player a
// big head for ten minutes and becomes an ordinary monster, wearing its event model when it has one.
public sealed partial class FieldInstance
{
    public const int HammerBlowsForHead = 10;
    public const long HammerHeadSeconds = 600;

    private readonly List<(MonsterInstance Monster, MonsterDefinition Form)> _eventForms = new();

    private void OnHammerLanded(MonsterInstance monster, WorldPlayer player)
    {
        var premium = player.Character.Premium;
        if (monster.HammerSpent || premium.BigHeadMode != 0 && premium.BigHeadExpiresAtSeconds > Premium.NowSeconds)
        {
            return;
        }

        if (monster.HammerTargetId != player.EntityId)
        {
            monster.HammerTargetId = player.EntityId;
            monster.HammerHits = 0;
        }

        if (++monster.HammerHits < HammerBlowsForHead)
        {
            return;
        }

        monster.HammerSpent = true;
        if (premium.TryStartBigHead(_rng.Range(1, 2), HammerHeadSeconds, Premium.NowSeconds))
        {
            player.Guard(EventFoods.HeadChangeGuardMs);
            ItemEvents.Add(new ItemEffectView(player.EntityId, 0, ItemEffectKind.HeadChange, player.Position, 0));
        }

        if (monster.Def.EventFormCode != 0 && _catalog.TryGet(monster.Def.EventFormCode, out var form))
        {
            _eventForms.Add((monster, form));
        }
    }

    // Swaps the monsters that took their event form, after every monster stepped; the form keeps the life left.
    private void ApplyEventForms()
    {
        foreach (var (monster, form) in _eventForms)
        {
            if (!monster.IsAlive || !RemoveMonster(monster.EntityId))
            {
                continue;
            }

            var changed = ((ISpawnSink)this).Spawn(form, monster.Position, monster.SpawnPointIndex, monster.Facing);
            changed.Life = Math.Min(monster.Life, form.Combat.MaxLife);
            changed.HammerSpent = true;
            changed.TargetId = monster.TargetId;
        }

        _eventForms.Clear();
    }
}
