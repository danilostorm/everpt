namespace PristonTale.Persistence.Abstractions.Characters;

public interface ICharacterRepository
{
    Task<IReadOnlyList<CharacterRecord>> ListByAccountAsync(long accountId, CancellationToken ct = default);
    Task<CharacterRecord?> FindByNameForAccountAsync(long accountId, string name, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);
    Task<IReadOnlyList<short>> UsedSlotsAsync(long accountId, CancellationToken ct = default);
    Task<CharacterRecord> CreateAsync(CharacterRecord record, CancellationToken ct = default);

    Task<bool> DeleteByNameAsync(long accountId, string name, CancellationToken ct = default);

    // False when no row took the record: the character is gone, or storage holds a newer capture (CharacterRecord.SaveSeq).
    Task<bool> UpdateAsync(CharacterRecord record, CancellationToken ct = default);

    // Background saves: every record in one transaction; a record of a character that is gone, or older than the stored
    // capture, is skipped. Returns the rows written.
    Task<int> SaveAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default);

    // Every record or none, in one transaction (a trade between characters).
    Task<bool> UpdateAllAsync(IReadOnlyList<CharacterRecord> records, CancellationToken ct = default);
}
