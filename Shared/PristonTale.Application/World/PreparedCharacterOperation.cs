using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Simulation.Characters;

namespace PristonTale.Application.World;

// A character change validated on the tick. A null Snapshot is final: nothing is written and the reply stands. Otherwise
// the snapshot is stored before the commit applies the candidate to the live character (DurableOperations).
public abstract record PreparedOperation(PlayerCharacter Original, PlayerCharacter? Candidate, CharacterSaveSnapshot? Snapshot);

public record PreparedCharacterOperation<TOutcome>(
    PlayerCharacter Original,
    PlayerCharacter? Candidate,
    CharacterSaveSnapshot? Snapshot,
    TOutcome Outcome) : PreparedOperation(Original, Candidate, Snapshot);
