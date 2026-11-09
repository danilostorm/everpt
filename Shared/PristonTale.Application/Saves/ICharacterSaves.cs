using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.Saves;

// Where a playing character's captures go. An ordinary change is committed in the world and stored in the background
// (Enqueue); the save of a character leaving the world waits until it is stored (SaveAsync). Each capture takes the
// session's next save sequence, so storage never replaces a newer capture with an older one.
public interface ICharacterSaves
{
    void Enqueue(PlayerSession session, CharacterSaveSnapshot snapshot);

    // True once stored, or kept in the save journal when the server stops before storage takes it.
    Task<bool> SaveAsync(PlayerSession session, CharacterSaveSnapshot snapshot);
}
