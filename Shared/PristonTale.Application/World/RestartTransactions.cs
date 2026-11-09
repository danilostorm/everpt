using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

public sealed record PreparedRestartOperation(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    RestartOutcome Outcome,
    int FieldId = 0,
    Vector3 Position = default)
    : PreparedCharacterOperation<RestartOutcome>(Original, Candidate, Snapshot, Outcome);

// Runs on the world tick. The candidate takes the penalties, the scroll and the restart life; the snapshot places the
// character where it stands up, so a restart is stored before the world applies it.
internal static class RestartTransactions
{
    public static PreparedRestartOperation Prepare(PlayerSession session, WorldPlayer player, FieldInstance field, WorldCatalog world, RestartCommand command,
        Random random)
    {
        var original = player.Character;
        if (!player.IsDead || session.Loaded is null)
        {
            return Final(original, command, RestartStatus.NotDead);
        }

        if (command.Choice is not (RestartChoice.Field or RestartChoice.Town or RestartChoice.Exit))
        {
            return Final(original, command, RestartStatus.Rejected);
        }

        var candidate = Resolve(original, field.State, command.Choice, out var experience, out var gold, out var scroll);
        if (candidate is null)
        {
            return Final(original, command, RestartStatus.FieldRefused);
        }

        int fieldId;
        Vector3 position;
        if (command.Choice == RestartChoice.Field)
        {
            fieldId = field.FieldId;
            position = world.TryGetFieldByCode(field.FieldId, out var here) ? NearestStart(here, player.Position) : player.Position;
        }
        else
        {
            fieldId = DeathPenalty.TownField(original.Class.Class);
            position = world.TryGetFieldByCode(fieldId, out var town) ? RandomStart(town, random) : player.Position;
        }

        var outcome = new RestartOutcome(RestartStatus.Restarted, command.Choice, experience, gold, scroll);
        var snapshot = CharacterRecordFactory.Capture(session.Loaded, candidate, fieldId, position);
        return new PreparedRestartOperation(original, candidate, snapshot, outcome, fieldId, position);
    }

    // The character after the restart's costs, or null when the field restart is refused: the scroll is used up, or the
    // death's share is taken; standing up in the field takes its own share unless the scroll was used. Life comes back at
    // half.
    public static PlayerCharacter? Resolve(PlayerCharacter original, FieldState field, RestartChoice choice, out ulong experience, out long gold, out bool scroll)
    {
        experience = 0;
        gold = 0;
        var candidate = original.CloneForInventoryOperation();
        var anchor = DeathPenalty.ScrollAnchor(candidate);
        scroll = anchor >= 0 && candidate.Inventory.ConsumeAt(anchor);
        if (!scroll)
        {
            Take(candidate, DeathPenalty.Divisor(candidate, field), ref experience, ref gold);
        }

        if (choice == RestartChoice.Field)
        {
            if (!DeathPenalty.CanRestartInField(candidate, field))
            {
                return null;
            }

            if (!scroll)
            {
                Take(candidate, DeathPenalty.FieldDivisor, ref experience, ref gold);
            }
        }

        candidate.Life = Math.Max(1, candidate.MaxLife / 2);
        return candidate;
    }

    private static void Take(PlayerCharacter character, int divisor, ref ulong experience, ref long gold)
    {
        experience += character.LoseExperience(DeathPenalty.ExperienceShare(character.Level, divisor));
        var share = DeathPenalty.GoldShare(character.Level, character.Money, divisor);
        if (share > 0 && character.TryChangeMoney(-share))
        {
            gold += share;
        }
    }

    // The field's start point nearest to where the player fell, as the original's field restart picks it.
    private static Vector3 NearestStart(FieldDef field, Vector3 fallen)
    {
        if (field.StartPoints.Count == 0)
        {
            return fallen;
        }

        var best = field.StartPoints[0];
        var bestSq = float.MaxValue;
        foreach (var point in field.StartPoints)
        {
            var dx = point.X - fallen.X;
            var dz = point.Z - fallen.Z;
            var distanceSq = dx * dx + dz * dz;
            if (distanceSq < bestSq)
            {
                best = point;
                bestSq = distanceSq;
            }
        }

        return new Vector3(best.X, float.NaN, best.Z);
    }

    private static Vector3 RandomStart(FieldDef field, Random random)
    {
        var point = field.StartPoints.Count > 0 ? field.StartPoints[random.Next(field.StartPoints.Count)] : field.Center;
        return new Vector3(point.X, float.NaN, point.Z);
    }

    private static PreparedRestartOperation Final(PlayerCharacter original, RestartCommand command, RestartStatus status) =>
        new(original, null, null, new RestartOutcome(status, command.Choice));
}
