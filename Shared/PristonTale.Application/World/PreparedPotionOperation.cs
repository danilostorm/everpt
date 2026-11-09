using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.World;

public sealed record PreparedPotionOperation(
    PlayerCharacter Original,
    PlayerCharacter Candidate,
    CharacterSaveSnapshot Snapshot,
    InventoryOutcome Outcome,
    PotionDefinition Potion)
    : PreparedCharacterOperation<InventoryOutcome>(Original, Candidate, Snapshot, Outcome);
