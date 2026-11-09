using PristonTale.Contracts.Social;

namespace PristonTale.Persistence.Abstractions.Contacts;

public sealed record ContactRecord(ContactList List, string Name);

// A character's friends and ignored players, one row per name; deleting the character deletes them.
public interface IContactRepository
{
    // Both lists, each in the order its names were added.
    Task<IReadOnlyList<ContactRecord>> ListAsync(long characterId, CancellationToken ct = default);

    // False when the name is already on that list.
    Task<bool> AddAsync(long characterId, ContactList list, string name, CancellationToken ct = default);

    Task<bool> RemoveAsync(long characterId, ContactList list, string name, CancellationToken ct = default);
}
