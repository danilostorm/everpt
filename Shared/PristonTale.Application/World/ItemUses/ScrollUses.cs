using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Application.World.ItemUses;

// Premium scrolls: one of each effect at a time, never beside the castle scroll of the same effect nor beside the
// opposite offensive or defensive scroll. The invulnerability scroll guards its user for its whole duration.
internal sealed class ScrollEffectUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var effects = use.Character.Effects;
        var scroll = ItemScrolls.For((ScrollEffectKind)use.Rule.Argument);
        if (effects.TryGet(scroll.StatusCode, out _))
        {
            return ItemUseStatus.AlreadyRunning;
        }

        if (CastleItems.All.Any(castle => castle.Kind == CastleItemKind.Scroll && castle.Target == (int)scroll.Kind
            && effects.TryGet(castle.StatusCode, out _)))
        {
            return ItemUseStatus.SameAttribute;
        }

        return ItemScrolls.Excluded(scroll.Kind) is { } excluded && effects.TryGet(ItemScrolls.For(excluded).StatusCode, out _)
            ? ItemUseStatus.NotHere
            : ItemUseStatus.Used;
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        var scroll = ItemScrolls.For((ScrollEffectKind)use.Rule.Argument);
        var durationMs = scroll.DurationSeconds * 1000f;
        use.Field.QueueItemEffect(use.Player.EntityId, scroll.Spec(), durationMs);
        if (scroll.Kind == ScrollEffectKind.Invulnerability)
        {
            use.Player.Guard(durationMs);
        }

        return ItemUseEffect.None;
    }
}

// Force orbs: one running orb at a time, within the orb's level band; the bonus is a share of the average attack power.
internal sealed class ForceOrbUse : ItemUseAction
{
    public override ItemUseStatus Check(in ItemUseRequest use)
    {
        var character = use.Character;
        ForceOrbs.TryGet(use.Item.Definition.CatalogKey, out var orb);
        if (character.Level < orb.MinimumLevel || character.Level > orb.MaximumLevel)
        {
            return ItemUseStatus.LevelTooLow;
        }

        foreach (var effect in character.Effects.Effects)
        {
            if (ForceOrbDefinition.IsOrbStatus(effect.Code))
            {
                return ItemUseStatus.AlreadyRunning;
            }
        }

        return ItemUseStatus.Used;
    }

    public override ItemUseEffect Apply(in ItemUseInWorld use)
    {
        if (!ForceOrbs.TryGet(use.Operation.CatalogKey, out var orb))
        {
            return ItemUseEffect.None;
        }

        use.Field.QueueItemEffect(use.Player.EntityId, orb.Spec(AverageAttack(use.Player.Character)), orb.DurationSeconds * 1000f);
        return new ItemUseEffect(ItemEffectKind.ForceOrb);
    }

    private static int AverageAttack(PlayerCharacter character)
    {
        var damage = WeaponDamage.Compute(character);
        return (damage.Min + damage.Max) / 2;
    }
}
