using System.Numerics;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Progression;
using PristonTale.Simulation.World;
using PristonTale.Contracts.Combat;
using PristonTale.Contracts.World;

namespace PristonTale.Application.World;

// A dead player standing up again (RestartTransactions): the tick prepares the costs and the place, the character is
// stored, then the tick moves the player there and revives it.
public sealed partial class WorldServer
{
    // Touched only by the tick thread: the town start point a restart lands on.
    private readonly Random _restartRandom = new();

    // The place carries the floor height there, so the client's FieldWarp lands on the same floor.
    public Task<PreparedRestartOperation?> PrepareRestartAsync(PlayerSession session, RestartCommand command)
    {
        return EnqueueAsync<PreparedRestartOperation?>(session.EntityId, () =>
        {
            if (!TryGetPlaying(session, out var connection) || !_fields.TryGetValue(connection.FieldId, out var field)
                || RestartTransactions.Prepare(session, connection.Player, field, _catalog, command, _restartRandom) is not { } prepared)
            {
                return null;
            }

            return prepared.Candidate is null || !_catalog.TryGetFieldByCode(prepared.FieldId, out _)
                ? prepared
                : prepared with { Position = _terrain.Get(prepared.FieldId).PlaceOnFloor(prepared.Position with { Y = 0f }) };
        });
    }

    // Exempt from the player's quota: the restart is already stored.
    public Task<bool> CommitRestartAsync(PlayerSession session, PreparedRestartOperation operation)
    {
        return EnqueueAsync(NoOwner, () =>
        {
            var original = operation.Original;
            if (!ReferenceEquals(session.Character, original) || operation.Candidate is not { } candidate)
            {
                throw new CommitInvariantException("Character ownership changed during restart commit.");
            }

            if (operation.Outcome.ScrollUsed)
            {
                original.CommitInventoryOperation(candidate);
            }

            original.RestoreProgress(candidate.Experience, candidate.Money, original.StatPoints, original.Tier, original.SkillPoints,
                original.EliteSkillPoints);
            if (!TryGetPlaying(session, out var connection) || !_catalog.TryGetFieldByCode(operation.FieldId, out var destination))
            {
                original.Life = candidate.Life;
                session.FieldId = operation.FieldId;
                session.SpawnPosition = operation.Position with { Y = 0f };
                return true;
            }

            var player = connection.Player;
            var inVillage = destination.State == FieldState.Village;
            Relocate(connection, operation.FieldId, operation.Position, inVillage);
            player.EnterField(operation.Position, inVillage);
            player.Restart(candidate.Life, DeathPenalty.RestartGuardMs);
            GetField(operation.FieldId).EmitCombatEvent(new CombatEventView(player.EntityId, player.EntityId, CombatEventKind.Revive,
                damage: player.Life));
            return true;
        }, waitForCapacity: true);
    }

    // The record a dead player is saved with: the town restart with its costs, so leaving while down neither skips the
    // penalty nor brings the player back dead.
    private CharacterSaveSnapshot? CaptureDead(PlayerSession session, PlayerCharacter character, int fieldId, Vector3 fellAt)
    {
        if (session.Loaded is null || !_fields.TryGetValue(fieldId, out var field)
            || RestartTransactions.Resolve(character, field.State, RestartChoice.Town, out _, out _, out _) is not { } candidate)
        {
            return null;
        }

        var town = DeathPenalty.TownField(candidate.Class.Class);
        var position = _catalog.TryGetFieldByCode(town, out var definition) && definition.StartPoints.Count > 0
            ? new Vector3(definition.StartPoints[0].X, 0f, definition.StartPoints[0].Z)
            : fellAt;
        return CharacterRecordFactory.Capture(session.Loaded, candidate, town, position);
    }
}
