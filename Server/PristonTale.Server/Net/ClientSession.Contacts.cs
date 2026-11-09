using PristonTale.Application.Contacts;
using PristonTale.Contracts.Social;
using PristonTale.Protocol.Packets;

namespace PristonTale.Server.Net;

// The friends page: a change or a refresh is answered with both lists.
internal sealed partial class ClientSession
{
    private async Task ChangeContactAsync(ContactChangePacket change)
    {
        if (_player is not null && _session is { } session)
        {
            SendContacts(await _services.Contacts.ChangeAsync(session.EntityId, change.List, change.Add, change.Name));
        }
    }

    private void RefreshContacts()
    {
        if (_player is not null && _session is { } session)
        {
            SendContacts(_services.Contacts.Lists(session.EntityId));
        }
    }

    private void SendContacts(ContactLists? lists)
    {
        if (lists is null)
        {
            return;
        }

        SendLarge(new ContactListPacket(ContactList.Friends, Wire(lists.Friends)).Write, ContactListPacket.MaxSize);
        SendLarge(new ContactListPacket(ContactList.Ignored, Wire(lists.Ignored)).Write, ContactListPacket.MaxSize);
    }

    private static ContactEntryWire[] Wire(IReadOnlyList<ContactEntry> entries) =>
        entries.Select(static entry => new ContactEntryWire(entry.Name, entry.Online)).ToArray();
}
