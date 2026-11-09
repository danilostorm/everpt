using PristonTale.Application.Chat;
using PristonTale.Contracts.Social;
using PristonTale.Persistence.Abstractions.Contacts;
using PristonTale.Simulation.Social;

namespace PristonTale.Application.Contacts;

// A name on a list as its owner sees it: a friend's presence (playing now); always false on the ignored list.
public readonly record struct ContactEntry(string Name, bool Online);

public sealed record ContactLists(IReadOnlyList<ContactEntry> Friends, IReadOnlyList<ContactEntry> Ignored);

// The friends page on the server: a character's lists are loaded when it joins the chat (ChatMember.Contacts), changed and
// stored one name at a time, and answered with each friend's presence. A refused change, one storage refused too, answers
// its line in the chat and the lists as they are.
// One connection's requests come one at a time, so a check and the change it allows never interleave.
public sealed class ContactAppService(ChatDirectory chat, IContactRepository contacts)
{
    public async Task<ContactBook> LoadAsync(long characterId, CancellationToken ct = default) =>
        new((await contacts.ListAsync(characterId, ct)).Select(static record => (record.List, record.Name)));

    public async Task<ContactLists?> ChangeAsync(ulong entityId, ContactList list, bool add, string name, CancellationToken ct = default)
    {
        if (!chat.TryGet(entityId, out var member) || member.Session.Loaded is not { } loaded)
        {
            return null;
        }

        var book = member.Contacts;
        name = name.Trim();
        var listed = add ? null : book.Find(list, name);
        var status = add ? book.Check(list, name, member.Name)
            : !book.Available ? ContactStatus.Unavailable
            : listed is null ? ContactStatus.NotListed
            : ContactStatus.Ok;
        if (status == ContactStatus.Ok)
        {
            status = await StoreAsync(loaded.Id, book, list, add, listed ?? name, ct);
        }

        if (status != ContactStatus.Ok)
        {
            member.Link.Deliver(ChatLine.Reply(ContactMessages.Of(status, name)));
        }

        return Lists(member);
    }

    // A change storage refuses leaves the lists as they were.
    private async Task<ContactStatus> StoreAsync(long characterId, ContactBook book, ContactList list, bool add, string name, CancellationToken ct)
    {
        try
        {
            if (add)
            {
                await contacts.AddAsync(characterId, list, name, ct);
                book.Add(list, name);
            }
            else
            {
                await contacts.RemoveAsync(characterId, list, name, ct);
                book.Remove(list, name);
            }

            return ContactStatus.Ok;
        }
        catch (System.Data.Common.DbException)
        {
            return ContactStatus.Unavailable;
        }
    }

    public ContactLists? Lists(ulong entityId) => chat.TryGet(entityId, out var member) ? Lists(member) : null;

    private ContactLists Lists(ChatMember member)
    {
        var book = member.Contacts;
        return new ContactLists(book.Friends.Select(name => new ContactEntry(name, chat.FindByName(name) is not null)).ToArray(),
            book.Ignored.Select(static name => new ContactEntry(name, false)).ToArray());
    }
}
