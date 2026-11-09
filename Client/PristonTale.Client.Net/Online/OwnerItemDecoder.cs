using PristonTale.Protocol.Packets;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Combat;
using PristonTale.Simulation.Items;

namespace PristonTale.Client.Net.Online;

// Owner item details arrive per inventory revision before its inventory parts; the equipment status arrives before
// each owner status. Both are folded into the owner state the presence decoder publishes.
internal sealed class OwnerItemDecoder
{
    private readonly Dictionary<int, ItemDetailEntry[]> _parts = new();
    private readonly Dictionary<Guid, ItemDetailView> _details = new();
    private long _partsRevision = -1;
    private long _detailsRevision = -1;
    private OwnerItemStatusPacket? _status;
    private IReadOnlyList<AgingProgressView> _aging = [];

    public void OnDetails(ItemDetailsPartPacket part)
    {
        if (part.Revision != _partsRevision)
        {
            _parts.Clear();
            _partsRevision = part.Revision;
        }

        _parts[part.Part] = part.Items.ToArray();
        if (_parts.Count < part.Parts)
        {
            return;
        }

        _details.Clear();
        foreach (var entry in _parts.Values.SelectMany(static items => items))
        {
            _details[entry.InstanceId] = View(entry);
        }

        _detailsRevision = part.Revision;
        _parts.Clear();
    }

    public void OnStatus(OwnerItemStatusPacket status) => _status = status;

    public void OnAging(AgingProgressPacket progress) =>
        _aging = progress.Entries.Select(static entry => new AgingProgressView(entry.InstanceId, entry.Count, entry.Goal)).ToArray();

    public IReadOnlyList<OwnedItemView> Attach(long revision, IReadOnlyList<OwnedItemView> items)
    {
        if (revision != _detailsRevision)
        {
            return items;
        }

        return items.Select(item => _details.TryGetValue(item.InstanceId, out var details) ? item with { Details = details } : item).ToArray();
    }

    public OwnerState Apply(OwnerState owner)
    {
        owner = owner with { Aging = _aging };
        if (_status is not { } status)
        {
            return owner;
        }

        var r = status.Resistances;
        return owner with
        {
            CarriedWeight = status.CarriedWeight,
            MaxWeight = status.MaxWeight,
            CriticalChance = status.CriticalChance,
            BlockChance = status.BlockChance,
            ShootingRange = status.ShootingRange,
            AttackSpeed = status.AttackSpeed,
            PotionSpace = status.PotionSpace,
            LifeRegen = status.LifeRegen,
            ManaRegen = status.ManaRegen,
            StaminaRegen = status.StaminaRegen,
            MagicMastery = status.MagicMastery,
            Resistances = new ResistanceBlock(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7])
        };
    }

    // The values of one copy as its wire entry carries them (owner items and the copies a personal shop offers).
    internal static ItemDetailView View(ItemDetailEntry e)
    {
        var r = e.Resistances;
        var spec = new ItemSpecBonus(e.SpecAbsorb, e.SpecDefence, e.SpecMoveSpeed, e.SpecBlock, e.SpecAttackSpeed, e.SpecCritical, e.SpecShootingRange,
            e.SpecMagicMastery, e.SpecLevelLife, e.SpecLevelMana, e.SpecLevelAttackRating, e.SpecLevelDamage, e.SpecLifeRegen, e.SpecManaRegen,
            e.SpecStaminaRegen);
        var roll = new ItemRoll(e.DamageMin, e.DamageMax, e.AttackRating, e.Defence, e.Absorb, e.Block, e.MoveSpeed, e.LifeRegen, e.ManaRegen,
            e.StaminaRegen, e.AddLife, e.AddMana, e.AddStamina, new ResistanceBlock(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7]),
            e.RequiredLevel, new CharacterAttributes(e.RequiredStrength, e.RequiredSpirit, e.RequiredTalent, e.RequiredAgility, e.RequiredHealth),
            e.Price, e.SpecJob, spec);
        return new ItemDetailView(e.DurabilityCurrent, e.DurabilityMax, e.UpgradeLevel, roll, e.Critical, e.AttackSpeed, e.ShootingRange,
            e.PotionSpace, e.MagicMastery, e.Weight, e.UniqueItem, (e.Flags & ItemDetailEntry.FlagTwoHanded) != 0,
            (e.Flags & ItemDetailEntry.FlagClassAllowed) != 0)
        {
            AgingCount = e.AgingCount,
            AgingGoal = e.AgingGoal,
            MixSheltom = e.MixSheltom,
            MixStats = (Simulation.Items.Mixing.MixStat)e.MixStats
        };
    }
}
