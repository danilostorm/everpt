using Dapper;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Contacts;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperContactRepository(IDbConnectionFactory connectionFactory) : IContactRepository
{
    public async Task<IReadOnlyList<ContactRecord>> ListAsync(long characterId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<(short List, string Name)>(new CommandDefinition(
            "SELECT list, name FROM character_contacts WHERE character_id = @characterId ORDER BY id", new { characterId }, cancellationToken: ct));
        return rows.Where(static row => Enum.IsDefined((ContactList)row.List)).Select(static row => new ContactRecord((ContactList)row.List, row.Name)).ToArray();
    }

    public async Task<bool> AddAsync(long characterId, ContactList list, string name, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO character_contacts (character_id, list, name) VALUES (@characterId, @list, @name)
            ON CONFLICT (character_id, list, name) DO NOTHING
            """,
            new { characterId, list = (short)list, name }, cancellationToken: ct)) > 0;
    }

    public async Task<bool> RemoveAsync(long characterId, ContactList list, string name, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM character_contacts WHERE character_id = @characterId AND list = @list AND lower(name) = lower(@name)",
            new { characterId, list = (short)list, name }, cancellationToken: ct)) > 0;
    }
}
