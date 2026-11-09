using PristonTale.Contracts.Social;

namespace PristonTale.Gameplay.UI.Contacts;

// The friends page's requests (GameScene), answered by the lists in the presence stream.
public interface IContactClient
{
    Task ChangeContactAsync(ContactList list, bool add, string name);

    Task RefreshContactsAsync();
}
