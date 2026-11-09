using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;
using PristonTale.Persistence.Abstractions.Warehouses;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperWarehouseRepository(IDbConnectionFactory connectionFactory) : IWarehouseRepository
{
    public async Task<WarehouseRecord?> FindAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<WarehouseRecord>(new CommandDefinition(
            "SELECT account_id, money, revision, items FROM warehouses WHERE account_id = @accountId",
            new { accountId }, cancellationToken: ct));
    }

    public async Task<bool> SaveWithCharacterAsync(CharacterRecord character, WarehouseRecord warehouse, long expectedRevision,
        CancellationToken ct = default)
    {
        if (character.AccountId != warehouse.AccountId)
        {
            throw new ArgumentException("The warehouse belongs to another account.", nameof(warehouse));
        }

        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var characterRows = await connection.ExecuteAsync(new CommandDefinition(
            DapperCharacterRepository.UpdateSql, character, transaction, cancellationToken: ct));
        if (characterRows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        var parameters = new
        {
            warehouse.AccountId,
            warehouse.Money,
            warehouse.Revision,
            warehouse.Items,
            Expected = expectedRevision
        };

        // No row counts as revision 0, so only that expectation may insert.
        var sql = expectedRevision == 0
            ? """
              INSERT INTO warehouses (account_id, money, revision, items)
              VALUES (@AccountId, @Money, @Revision, @Items)
              ON CONFLICT (account_id) DO UPDATE SET
                  money = excluded.money, revision = excluded.revision, items = excluded.items
              WHERE warehouses.revision = 0
              """
            : """
              UPDATE warehouses SET money = @Money, revision = @Revision, items = @Items
              WHERE account_id = @AccountId AND revision = @Expected
              """;
        var warehouseRows = await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
        if (warehouseRows == 0)
        {
            await transaction.RollbackAsync(ct);
            return false;
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}
