using PristonTale.Application.Saves;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Tests;

// Stores each capture at once, so a case reads storage right after the operation that queued it.
internal sealed class ImmediateSaves(ICharacterRepository characters) : ICharacterSaves
{
    public void Enqueue(PlayerSession session, CharacterSaveSnapshot snapshot) =>
        characters.UpdateAsync(session.Stamp(snapshot)).GetAwaiter().GetResult();

    public Task<bool> SaveAsync(PlayerSession session, CharacterSaveSnapshot snapshot) => characters.UpdateAsync(session.Stamp(snapshot));
}
