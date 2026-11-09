namespace PristonTale.Contracts.World;

// Where a dead player chooses to stand up again: in the field where it fell, in its town, or in its town while the
// client quits.
public enum RestartChoice : byte
{
    Field = 1,
    Town = 2,
    Exit = 3
}

public enum RestartStatus : byte
{
    Restarted = 0,
    NotDead = 1,
    // The field restart is not open here or the experience above the level's start does not cover it.
    FieldRefused = 2,
    Busy = 3,
    StorageFailed = 4,
    Rejected = 5
}

public readonly record struct RestartCommand(Guid OperationId, RestartChoice Choice);

// What the restart took: the death's share and, in the field, the field restart's share; a carried Resurrection Scroll
// is used up instead.
public sealed record RestartOutcome(RestartStatus Status, RestartChoice Choice, ulong ExperienceLost = 0, long GoldLost = 0, bool ScrollUsed = false)
{
    public bool Ok => Status == RestartStatus.Restarted;
}
