using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Abstractions.Warehouses;

// Items hold SavedItems JSON whose anchors are cells of the warehouse grid.
public sealed record WarehouseRecord(long AccountId, long Money, long Revision, JsonText Items);

public interface IWarehouseRepository
{
    Task<WarehouseRecord?> FindAsync(long accountId, CancellationToken ct = default);

    // Writes the character and its account warehouse in one transaction. The warehouse row must still be at
    // expectedRevision (no row counts as revision 0); otherwise nothing is written and the result is false.
    Task<bool> SaveWithCharacterAsync(CharacterRecord character, WarehouseRecord warehouse, long expectedRevision, CancellationToken ct = default);
}
