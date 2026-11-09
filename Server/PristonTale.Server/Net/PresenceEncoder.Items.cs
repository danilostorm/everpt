using PristonTale.Application.World;
using PristonTale.Protocol.Binary;
using PristonTale.Protocol.Packets;
using PristonTale.Protocol.Transport;
using PristonTale.Simulation.Characters;
using PristonTale.Contracts.Units;

namespace PristonTale.Server.Net;

// Item effects of the receiver and the players it sees, and the receiver's premium timers whenever they change.
internal sealed partial class PresenceEncoder
{
    private readonly List<ItemEffectEntry> _itemEffects = new();
    private long _premiumRevision = -1;

    private static ItemEffectEntry ItemEffect(in ItemEffectView effect) => new(effect.EntityId, effect.ItemCode, (byte)effect.Effect,
        GameUnits.ToFixed(effect.Position.X), GameUnits.ToFixed(effect.Position.Y), GameUnits.ToFixed(effect.Position.Z), effect.Variant,
        effect.Target);

    private void SendItemEffects(ReadOnlySpan<ItemEffectView> effects, IPacketSink connection, List<int>? routed = null)
    {
        _itemEffects.Clear();
        if (routed is not null)
        {
            foreach (var index in routed)
            {
                _itemEffects.Add(ItemEffect(effects[index]));
            }
        }
        else
        {
            foreach (var effect in effects)
            {
                if (Relevant(effect.EntityId, 0))
                {
                    _itemEffects.Add(ItemEffect(effect));
                }
            }
        }

        if (_itemEffects.Count == 0)
        {
            return;
        }

        SendClock(connection);
        for (var offset = 0; offset < _itemEffects.Count; offset += ItemEffectsPacket.MaxEffects)
        {
            var batch = _itemEffects.GetRange(offset, Math.Min(ItemEffectsPacket.MaxEffects, _itemEffects.Count - offset));
            connection.Send(_scratch.AsSpan(0, new ItemEffectsPacket(batch).Write(_scratch)));
        }
    }

    private void SendPremium(OwnerState? owner, IPacketSink connection)
    {
        if (owner is null || owner.PremiumRevision == _premiumRevision)
        {
            return;
        }

        _premiumRevision = owner.PremiumRevision;
        SendTimers(owner, connection);
    }

    // Its own method: the lambda captures the owner, and a capture in SendPremium would allocate on every call.
    private void SendTimers(OwnerState owner, IPacketSink connection)
    {
        var timers = owner.PremiumTimers.Take(PremiumTimersPacket.MaxTimers)
            .Select(timer => new PremiumTimerEntry((byte)timer.Kind, timer.Value, (uint)Math.Clamp(timer.ExpiresAtSeconds - owner.PremiumClockSeconds, 0, uint.MaxValue),
                (uint)Math.Clamp(timer.DurationSeconds, 0, uint.MaxValue)))
            .ToArray();
        connection.Send(_scratch.AsSpan(0, new PremiumTimersPacket(owner.PremiumRevision, timers).Write(_scratch)));
    }
}
