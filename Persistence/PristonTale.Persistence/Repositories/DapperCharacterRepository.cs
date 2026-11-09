using Dapper;
using PristonTale.Persistence.Abstractions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Persistence.Repositories;

public sealed class DapperCharacterRepository(IDbConnectionFactory connectionFactory) : ICharacterRepository
{
    // The full column projection shared by every read and the INSERT/RETURNING. The JSON columns
    // (inventory/equipment/skills/quests/premium) bind through the per-provider
    // JsonText type handler (Postgres jsonb / SQLite TEXT), so the SQL here stays provider-agnostic.
    private const string Columns =
        "id, account_id, name, slot, job_code, level, exp, money, brood, " +
        "strength, spirit, talent, agility, health, stat_points, " +
        "life, mana, stamina, field_id, pos_x, pos_z, model_name, model_name2, face_index, " +
        "tier, hair_index, size_level, " +
        "inventory, equipment, skills, quests, premium, last_played_at, save_seq, starter_gifts_received, help_pet, help_pet_hidden";

    // Transactions that write several characters lock them in id order, so two of them never wait on each other.
    // Identity columns (id/account_id/name/slot/job_code/created_at) are immutable and never updated. A capture older than
    // the stored one (a lower save_seq) changes nothing; the same capture written again is harmless.
    internal const string UpdateSql =
        """
            UPDATE characters SET
                level = @Level, exp = @Exp, money = @Money, brood = @Brood,
                strength = @Strength, spirit = @Spirit, talent = @Talent, agility = @Agility,
                health = @Health, stat_points = @StatPoints,
                life = @Life, mana = @Mana, stamina = @Stamina,
                field_id = @FieldId, pos_x = @PosX, pos_z = @PosZ,
                model_name = @ModelName, model_name2 = @ModelName2, face_index = @FaceIndex,
                tier = @Tier, hair_index = @HairIndex, size_level = @SizeLevel,
                inventory = @Inventory, equipment = @Equipment, skills = @Skills,
                quests = @Quests, premium = @Premium,
                last_played_at = @LastPlayedAt, save_seq = @SaveSeq, starter_gifts_received = @StarterGiftsReceived,
                help_pet = @HelpPet, help_pet_hidden = @HelpPetHidden
            WHERE id = @Id AND save_seq <= @SaveSeq
            """;

    public async Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.QueryAsync<CharacterRecord>(new CommandDefinition(
            $"SELECT {Columns} FROM characters WHERE account_id = @accountId ORDER BY slot",
            new { accountId }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CharacterRecord>(new CommandDefinition(
            $"SELECT {Columns} FROM characters WHERE account_id = @accountId AND name = @name",
            new { accountId, name }, cancellationToken: ct));
    }

    public async Task<bool> NameExistsAsync(string name, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM characters WHERE name = @name)",
            new { name }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var slots = await connection.QueryAsync<short>(new CommandDefinition(
            "SELECT slot FROM characters WHERE account_id = @accountId",
            new { accountId }, cancellationToken: ct));
        return slots.ToList();
    }

    public async Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        return await connection.QuerySingleAsync<CharacterRecord>(new CommandDefinition(
            $"""
            INSERT INTO characters (
                account_id, name, slot, job_code, level, exp, money, brood,
                strength, spirit, talent, agility, health, stat_points,
                life, mana, stamina, field_id, pos_x, pos_z, model_name, model_name2, face_index,
                tier, hair_index, size_level,
                inventory, equipment, skills, quests, premium, starter_gifts_received, help_pet, help_pet_hidden)
            VALUES (
                @AccountId, @Name, @Slot, @JobCode, @Level, @Exp, @Money, @Brood,
                @Strength, @Spirit, @Talent, @Agility, @Health, @StatPoints,
                @Life, @Mana, @Stamina, @FieldId, @PosX, @PosZ, @ModelName, @ModelName2, @FaceIndex,
                @Tier, @HairIndex, @SizeLevel,
                @Inventory, @Equipment, @Skills, @Quests, @Premium, @StarterGiftsReceived, @HelpPet, @HelpPetHidden)
            RETURNING {Columns}
            """,
            record, cancellationToken: ct));
    }

    public async Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM characters WHERE account_id = @accountId AND name = @name",
            new { accountId, name }, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            UpdateSql,
            record, cancellationToken: ct));
        return rows > 0;
    }

    public async Task<int> SaveAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var written = 0;
        foreach (var record in records.OrderBy(static record => record.Id))
        {
            written += await connection.ExecuteAsync(new CommandDefinition(UpdateSql, record, transaction, cancellationToken: ct));
        }

        await transaction.CommitAsync(ct);
        return written;
    }

    public async Task<bool> UpdateAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.OpenAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var record in records.OrderBy(static record => record.Id))
        {
            if (await connection.ExecuteAsync(new CommandDefinition(UpdateSql, record, transaction, cancellationToken: ct)) == 0)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }
        }

        await transaction.CommitAsync(ct);
        return true;
    }
}
