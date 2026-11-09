using PristonTale.Persistence.Abstractions.GameLogs;
using PristonTale.Simulation.Characters;
using PristonTale.Simulation.Items;

namespace PristonTale.Application.GameLogs;

// The rows of one transfer. A transfer's items are what its operation took out of or put into a character: every item
// the character owns (bag, equipment, belt, quest box) before the commit compared with its candidate, by instance id
// and count. It runs on the tick at a transfer's commit only.
internal sealed class TransferLog(ItemTransferKind kind, string transferId, int fieldId, DateTime occurredAt)
{
    public List<ItemTransferRecord> Rows { get; } = [];

    // The items, or parts of stacks, the candidate no longer has.
    public void Left(PlayerCharacter before, PlayerCharacter after, long? from, long? to, long? account = null)
    {
        var kept = Counts(after);
        foreach (var item in before.OwnedItems())
        {
            var count = item.Count - kept.GetValueOrDefault(item.InstanceId);
            if (count > 0)
            {
                Item(from, to, account, item, count);
            }
        }
    }

    // The items, or parts of stacks, the candidate gained.
    public void Arrived(PlayerCharacter before, PlayerCharacter after, long? from, long? to, long? account = null)
    {
        var had = Counts(before);
        foreach (var item in after.OwnedItems())
        {
            var count = item.Count - had.GetValueOrDefault(item.InstanceId);
            if (count > 0)
            {
                Item(from, to, account, item, count);
            }
        }
    }

    public void Item(long? from, long? to, long? account, ItemInstance item, int count) =>
        Rows.Add(new ItemTransferRecord(occurredAt, kind, transferId, from, to, account, fieldId, item.Definition.CatalogKey,
            item.InstanceId.ToString(), count));

    public void Gold(long? from, long? to, long gold, long? account = null)
    {
        if (gold > 0)
        {
            Rows.Add(new ItemTransferRecord(occurredAt, kind, transferId, from, to, account, fieldId, null, null, gold));
        }
    }

    private static Dictionary<Guid, int> Counts(PlayerCharacter character)
    {
        var counts = new Dictionary<Guid, int>();
        foreach (var item in character.OwnedItems())
        {
            counts[item.InstanceId] = counts.GetValueOrDefault(item.InstanceId) + item.Count;
        }

        return counts;
    }
}
