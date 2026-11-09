namespace PristonTale.Contracts.World;

public enum WingGateStatus : byte
{
    Warped = 0,
    Rejected = 1,
    NotAtStone = 2,
    NotReached = 3,
    LevelTooLow = 4,
    NotEnoughGold = 5,
    Busy = 6,
    StorageFailed = 7
}

public readonly record struct WingGateCommand(Guid OperationId, int Destination);

public sealed record WingGateOutcome(WingGateStatus Status, int Destination, long Fee = 0)
{
    public bool Ok => Status == WingGateStatus.Warped;
}
