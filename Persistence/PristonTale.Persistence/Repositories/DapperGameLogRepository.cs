using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.GameLogs;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperGameLogRepository(IDbConnectionFactory connectionFactory) : IGameLogRepository
{
    private static readonly string[] Tables = ["item_transfers", "currency_sinks", "server_events"];

    public async Task AppendItemTransfersAsync(IReadOnlyList<ItemTransferRecord> transfers, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO item_transfers (occurred_at, kind, transfer_id, from_character_id, to_character_id, account_id, field_id, item_code,
                instance_id, amount)
            VALUES (@OccurredAt, @Kind, @TransferId, @FromCharacterId, @ToCharacterId, @AccountId, @FieldId, @ItemCode, @InstanceId, @Amount)
            """,
            transfers.Select(static transfer => new
            {
                transfer.OccurredAt, Kind = (short)transfer.Kind, transfer.TransferId, transfer.FromCharacterId, transfer.ToCharacterId,
                transfer.AccountId, transfer.FieldId, transfer.ItemCode, transfer.InstanceId, transfer.Amount,
            }), transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    public async Task AppendCurrencyAsync(IReadOnlyList<CurrencySinkRecord> sinks, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO currency_sinks (occurred_at, sink, gold) VALUES (@OccurredAt, @Sink, @Gold)", sinks, transaction, cancellationToken: ct));
        await transaction.CommitAsync(ct);
    }

    public async Task AppendServerEventAsync(ServerEventRecord serverEvent, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO server_events (occurred_at, kind, server_name, detail) VALUES (@OccurredAt, @Kind, @ServerName, @Detail)",
            new { serverEvent.OccurredAt, Kind = (short)serverEvent.Kind, serverEvent.ServerName, serverEvent.Detail }, cancellationToken: ct));
    }

    public async Task<int> PruneAsync(DateTime before, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var deleted = 0;
        foreach (var table in Tables)
        {
            deleted += await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {table} WHERE occurred_at < @before", new { before }, cancellationToken: ct));
        }

        return deleted;
    }
}
