using System.Text.Json;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items.Usable;

namespace PristonTale.Persistence.Abstractions.Characters;

public sealed record SavedPremiumTimer(PremiumEffectKind Kind, int Value, long DurationSeconds, long ExpiresAtSeconds);

// The character's `premium` column: the running premium timers with their real-clock expiry (Unix seconds) and the
// recent item uses kept for retries.
// SavedAtSeconds is the real clock of the save: timers that pause offline move their expiry by the time between the
// save and the next entry.
public sealed record SavedPremium(int Version, SavedPremiumTimer[] Timers, AppliedItemUse[]? Operations = null, int GravityScrolls = 0,
    int BigHeadMode = 0, long BigHeadExpiresAtSeconds = 0, long SavedAtSeconds = 0)
{
    public static SavedPremium Capture(PlayerCharacter character)
    {
        var timers = character.Premium.Timers
            .Select(static timer => new SavedPremiumTimer(timer.Kind, timer.Value, timer.DurationSeconds, timer.ExpiresAtSeconds))
            .ToArray();
        var premium = character.Premium;
        return new SavedPremium(1, timers, StoredHistory.Newest(character.ItemUses), premium.GravityScrolls, premium.BigHeadMode,
            premium.BigHeadExpiresAtSeconds, premium.ClockSeconds);
    }

    // Expired timers are dropped while loading; an unknown kind or version is corrupt data.
    public static void Restore(PlayerCharacter character, JsonText json, long nowSeconds)
    {
        if (CharacterStateCodec.IsBlank(json, JsonValueKind.Object))
        {
            character.Premium.Restore([]);
            character.ItemUses.Restore([], static _ => true, "item use");
            return;
        }

        var state = JsonSerializer.Deserialize<SavedPremium>(json.Value, CharacterStateCodec.Options);
        if (state is null || state.Version != 1 || state.Timers is null || state.Timers.Length > PremiumTimers.MaxTimers)
        {
            throw new InvalidDataException("Unsupported premium-state version or size.");
        }

        var offline = state.SavedAtSeconds > 0 ? Math.Max(0, nowSeconds - state.SavedAtSeconds) : 0;
        character.Premium.Restore(state.Timers
            .Where(static timer => timer is not null)
            .Select(timer => new PremiumTimer(timer.Kind, timer.Value, timer.DurationSeconds,
                PremiumTimers.PausesOffline(timer.Kind) ? timer.ExpiresAtSeconds + offline : timer.ExpiresAtSeconds))
            .Where(timer => timer.ExpiresAtSeconds > nowSeconds));
        character.ItemUses.Restore(state.Operations ?? [], static applied => applied.Outcome is not null, "item use");
        var bigHead = state.BigHeadExpiresAtSeconds > nowSeconds ? state.BigHeadMode : 0;
        character.Premium.RestoreCounters(state.GravityScrolls, bigHead, state.BigHeadExpiresAtSeconds);
    }
}
