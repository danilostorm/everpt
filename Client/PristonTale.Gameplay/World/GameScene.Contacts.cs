using PristonTale.Contracts.Social;
using PristonTale.Gameplay.UI.Contacts;

namespace PristonTale.Gameplay.World;

// Contact list requests through the world connection.
public sealed partial class GameScene : IContactClient
{
    public Task ChangeContactAsync(ContactList list, bool add, string name) =>
        SendSocial(_playChannel?.ChangeContactAsync(list, add, name, _presenceCts.Token), "contact change");

    public Task RefreshContactsAsync() => SendSocial(_playChannel?.RefreshContactsAsync(_presenceCts.Token), "contact refresh");
}
