using PristonTale.Simulation.Characters;

namespace PristonTale.Simulation.PersonalShops;

// Carried on the wire; append values only.
public enum PersonalShopStatus : ushort
{
    Ok = 0,
    Rejected = 1,
    NotInTown = 2,
    NotListable = 3,
    // The seller could not carry the gold its listing or this sale brings.
    GoldLimit = 4,
    // The shop closed, or its seller left.
    Closed = 5,
    SoldOut = 6,
    // Fewer units remain than asked; the reply names how many.
    CountChanged = 7,
    // The seller asks another price now; the reply names it.
    PriceChanged = 8,
    NotEnoughGold = 9,
    NoSpace = 10,
    OverWeight = 11,
    Busy = 12,
    StorageFailed = 13,
    Stale = 14,
    OutOfReach = 15,
    Conflict = 16,
    // The seller's character is held by its shop.
    Selling = 17
}

// A buyer's request for Count units of a listed item at the unit price it was shown, against its own inventory revision.
public sealed record PersonalShopPurchase(Guid OperationId, ulong SellerId, Guid ItemId, long UnitPrice, int Count, long ExpectedRevision);

public sealed record AppliedShopPurchase(PersonalShopPurchase Purchase, long Revision) : IAppliedOperation
{
    Guid IAppliedOperation.OperationId => Purchase?.OperationId ?? Guid.Empty;
}
