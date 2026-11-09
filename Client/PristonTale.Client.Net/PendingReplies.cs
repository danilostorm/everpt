using System.Collections.Concurrent;

namespace PristonTale.Client.Net;

// Requests waiting for the reply that echoes their key. A reply nobody waits for is dropped; FailAll ends every waiter
// when the connection goes. A waiter leaves the table when its reply, its cancellation or the failure ends it.
internal sealed class PendingReplies<TKey, TReply> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TaskCompletionSource<TReply>> _waiting = new();

    public bool IsWaiting(TKey key) => _waiting.ContainsKey(key);

    // Registers the key, sends the request, then waits for its reply. Two requests with one key are a caller error.
    public async Task<TReply> RequestAsync(TKey key, Action send, CancellationToken ct)
    {
        var waiter = new TaskCompletionSource<TReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_waiting.TryAdd(key, waiter))
        {
            throw new InvalidOperationException($"A request with key {key} is already waiting for its reply.");
        }

        try
        {
            send();
            return await waiter.Task.WaitAsync(ct);
        }
        finally
        {
            _waiting.TryRemove(new KeyValuePair<TKey, TaskCompletionSource<TReply>>(key, waiter));
        }
    }

    public void Complete(TKey key, TReply reply)
    {
        if (_waiting.TryGetValue(key, out var waiter))
        {
            waiter.TrySetResult(reply);
        }
    }

    public void FailAll(Exception error)
    {
        foreach (var waiter in _waiting.Values)
        {
            waiter.TrySetException(error);
        }
    }
}
