using System.Numerics;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Client.Net.Presence;

public sealed record PremiumTimerInfo(PremiumEffectKind Kind, int Value, long RemainingSeconds, long DurationSeconds);

// Every running premium timer of the local player at the time the server sent them.
public sealed record PremiumTimerSnapshot(long Revision, IReadOnlyList<PremiumTimerInfo> Timers);

// An item effect around an entity; Position is in transport coordinates (PT-global, Z not flipped).
public sealed record ItemEffectInfo(ulong Entity, uint ItemCode, ItemEffectKind Effect, Vector3 Position, int Variant, ulong Target = 0);
