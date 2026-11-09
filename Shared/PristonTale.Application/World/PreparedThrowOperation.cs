using System.Numerics;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

public sealed record PreparedThrowOperation(
    PlayerCharacter Original,
    PlayerCharacter Candidate,
    CharacterSaveSnapshot Snapshot,
    InventoryOutcome Outcome,
    FieldInstance Field,
    ItemInstance? Item,
    int Gold,
    Vector3 Position,
    ulong ThrowerId)
    : PreparedCharacterOperation<InventoryOutcome>(Original, Candidate, Snapshot, Outcome);
