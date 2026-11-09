using PristonTale.Application.GameLogs;
using PristonTale.Persistence.Abstractions.GameLogs;

namespace PristonTale.Tests;

// Keeps the item transfers the world reports, in order.
internal sealed class RecordingLog : IGameLog
{
    public List<ItemTransferRecord> Rows { get; } = [];

    public void ItemTransfers(IReadOnlyList<ItemTransferRecord> transfers)
    {
        lock (Rows)
        {
            Rows.AddRange(transfers);
        }
    }
}
