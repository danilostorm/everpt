namespace PristonTale.Persistence.Abstractions.Characters;

// Operation histories answer a retried operation id with its first result. After a reload the client retries only the
// few commands it sent last, so storage keeps the newest Count entries of each history; memory keeps up to
// OperationHistory.Capacity.
internal static class StoredHistory
{
    public const int Count = 16;

    public static T[] Newest<T>(IReadOnlyList<T> history)
    {
        var skip = Math.Max(0, history.Count - Count);
        var newest = new T[history.Count - skip];
        for (var index = 0; index < newest.Length; index++)
        {
            newest[index] = history[skip + index];
        }

        return newest;
    }
}
