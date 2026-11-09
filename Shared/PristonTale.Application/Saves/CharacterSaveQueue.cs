using System.Diagnostics;
using System.Threading.Channels;
using PristonTale.Application.Services;
using PristonTale.Application.Sessions;
using PristonTale.Persistence.Abstractions.Characters;

namespace PristonTale.Application.Saves;

public readonly record struct SaveQueueStats(int Pending, long Written, long Batches, long Failures, long Dropped, double LastBatchMs);

// The background store of playing characters. A character keeps only its newest pending capture, and one writer stores
// up to batchSize characters per transaction, the captures a leaving character waits for first.
//
// A batch storage refuses is tried again one character at a time. When the first of them is refused too, storage is
// unavailable and the batch waits, backing off from firstBackoff (RetryBackoff), until storage takes it. A capture
// refused alone counts a refusal only when storage answers an empty write (it is reachable), so an unavailable storage
// never counts; at MaxRefusals it is one storage can never take, and it is dropped (reported, its waiters answered
// false) so it cannot hold the others or the next start. Once the server is stopping, the first failure sends everything left to the journal without another
// attempt, which the next start stores before players enter (ReplayAsync).
public sealed class CharacterSaveQueue(
    ICharacterRepository characters,
    SaveJournal journal,
    int batchSize = CharacterSaveQueue.DefaultBatchSize,
    TimeSpan? firstBackoff = null) : ICharacterSaves
{
    public const int DefaultBatchSize = 100;
    private const int MaxRefusals = 3;

    private readonly object _gate = new();
    private readonly Dictionary<long, Pending> _pending = new();
    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private bool _closed;
    private bool _journalOnly;
    private long _written;
    private long _batches;
    private long _failures;
    private long _dropped;
    private long _lastBatchTicks;

    // A character's newest capture waiting for storage, the leaves waiting for it (a newer capture includes theirs) and
    // how often storage refused it alone.
    private sealed class Pending(long seq, CharacterSaveSnapshot snapshot)
    {
        public long Seq = seq;
        public CharacterSaveSnapshot Snapshot = snapshot;
        public List<TaskCompletionSource<bool>>? Waiters;
        public int Refusals;
        public CharacterRecord? Record;
    }

    public Action<Exception>? Failed { get; init; }

    public SaveQueueStats Stats
    {
        get
        {
            int pending;
            lock (_gate)
            {
                pending = _pending.Count;
            }

            return new SaveQueueStats(pending, Interlocked.Read(ref _written), Interlocked.Read(ref _batches), Interlocked.Read(ref _failures),
                Interlocked.Read(ref _dropped), Stopwatch.GetElapsedTime(0, Interlocked.Read(ref _lastBatchTicks)).TotalMilliseconds);
        }
    }

    public void Enqueue(PlayerSession session, CharacterSaveSnapshot snapshot) => Add(session.NextSaveSeq(), snapshot, null);

    public Task<bool> SaveAsync(PlayerSession session, CharacterSaveSnapshot snapshot)
    {
        var stored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Add(session.NextSaveSeq(), snapshot, stored);
        return stored.Task;
    }

    // Stores the journal a stopped server left, oldest file first; a file is deleted once storage took it. A file that
    // cannot be read, and the records a reachable storage refuses, are set aside (".unreadable", ".refused") and reported,
    // so they never hold the start; when storage is unreachable the replay throws, to be tried again.
    public async Task ReplayAsync(CancellationToken ct)
    {
        foreach (var file in journal.Files())
        {
            IReadOnlyList<CharacterRecord> records;
            try
            {
                records = SaveJournal.Read(file);
            }
            catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidDataException)
            {
                Failed?.Invoke(exception);
                File.Move(file, file + ".unreadable", overwrite: true);
                continue;
            }

            try
            {
                await characters.SaveAllAsync(records, ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await characters.SaveAllAsync([], ct);
                var refused = new List<CharacterRecord>();
                foreach (var record in records)
                {
                    try
                    {
                        await characters.SaveAllAsync([record], ct);
                    }
                    catch (Exception single) when (single is not OperationCanceledException)
                    {
                        refused.Add(record);
                    }
                }

                Failed?.Invoke(exception);
                File.WriteAllText(file + ".refused", System.Text.Json.JsonSerializer.Serialize(refused));
            }

            File.Delete(file);
        }
    }

    // Stores until stop is requested and nothing is pending.
    public async Task RunAsync(CancellationToken stopping, CancellationToken stop)
    {
        using var either = CancellationTokenSource.CreateLinkedTokenSource(stopping, stop);
        var backoff = new RetryBackoff(firstBackoff);
        while (true)
        {
            var batch = Take();
            if (batch.Count == 0)
            {
                if (stop.IsCancellationRequested)
                {
                    var rest = Close();
                    Journal(_journalOnly || rest.Count == 0 ? rest : await StoreAsync(rest));
                    return;
                }

                try
                {
                    await _signal.Reader.WaitToReadAsync(stop);
                    _signal.Reader.TryRead(out _);
                }
                catch (OperationCanceledException)
                {
                }

                continue;
            }

            if (_journalOnly)
            {
                Journal(batch);
                continue;
            }

            var unstored = await StoreAsync(batch);
            if (unstored.Count == 0)
            {
                backoff.Reset();
                continue;
            }

            if (stopping.IsCancellationRequested || stop.IsCancellationRequested)
            {
                _journalOnly = true;
                Journal(unstored);
                continue;
            }

            Requeue(unstored);
            try
            {
                await Task.Delay(backoff.Next(), either.Token);
            }
            catch (OperationCanceledException)
            {
            }

        }
    }

    private void Add(long seq, CharacterSaveSnapshot snapshot, TaskCompletionSource<bool>? waiter)
    {
        lock (_gate)
        {
            if (!_closed)
            {
                if (_pending.TryGetValue(snapshot.Scalars.Id, out var pending))
                {
                    if (seq > pending.Seq)
                    {
                        pending.Seq = seq;
                        pending.Snapshot = snapshot;
                        pending.Record = null;
                    }
                }
                else
                {
                    _pending[snapshot.Scalars.Id] = pending = new Pending(seq, snapshot);
                }

                if (waiter is not null)
                {
                    (pending.Waiters ??= []).Add(waiter);
                }

                _signal.Writer.TryWrite(true);
                return;
            }
        }

        // The writer has stopped: the capture goes straight to the journal.
        var late = new Pending(seq, snapshot);
        if (waiter is not null)
        {
            late.Waiters = [waiter];
        }

        Journal([late]);
    }

    private List<Pending> Take()
    {
        var batch = new List<Pending>();
        lock (_gate)
        {
            foreach (var waited in (ReadOnlySpan<bool>)[true, false])
            {
                foreach (var pending in _pending.Values)
                {
                    if (batch.Count == batchSize)
                    {
                        break;
                    }

                    if ((pending.Waiters is not null) == waited)
                    {
                        batch.Add(pending);
                    }
                }
            }

            foreach (var pending in batch)
            {
                _pending.Remove(pending.Snapshot.Scalars.Id);
            }
        }

        return batch;
    }

    // Whichever of a batch capture and a capture that arrived meanwhile is newer stays, with the waiters of both.
    private void Requeue(List<Pending> batch)
    {
        lock (_gate)
        {
            foreach (var pending in batch)
            {
                if (_pending.TryGetValue(pending.Snapshot.Scalars.Id, out var other))
                {
                    var (newer, older) = other.Seq > pending.Seq ? (other, pending) : (pending, other);
                    if (older.Waiters is not null)
                    {
                        (newer.Waiters ??= []).AddRange(older.Waiters);
                    }

                    _pending[newer.Snapshot.Scalars.Id] = newer;
                }
                else
                {
                    _pending[pending.Snapshot.Scalars.Id] = pending;
                }
            }
        }
    }

    private List<Pending> Close()
    {
        lock (_gate)
        {
            _closed = true;
            var rest = _pending.Values.ToList();
            _pending.Clear();
            return rest;
        }
    }

    // Stores a batch and returns what is left unstored, to try again later.
    private async Task<List<Pending>> StoreAsync(List<Pending> batch)
    {
        var encoded = Encode(batch);
        if (encoded.Count == 0 || encoded.Count > 1 && await WriteAsync(encoded))
        {
            return [];
        }

        // The captures refused alone before are tried last, so they cannot shut out the others; a first refusal with
        // nothing stored yet means storage is unavailable, and the rest is not tried.
        var stored = false;
        var refusedAlone = false;
        var unstored = new List<Pending>();
        foreach (var pending in encoded.OrderBy(static pending => pending.Refusals))
        {
            if (!stored && refusedAlone)
            {
                unstored.Add(pending);
            }
            else if (await WriteAsync([pending]))
            {
                stored = true;
            }
            else
            {
                refusedAlone = true;
                if (!await RefuseAsync(pending))
                {
                    unstored.Add(pending);
                }
            }
        }

        return unstored;
    }

    // Counts a refusal of a capture tried alone when storage is reachable, and drops the capture at MaxRefusals; true
    // when dropped.
    private async Task<bool> RefuseAsync(Pending pending)
    {
        try
        {
            await characters.SaveAllAsync([], CancellationToken.None);
        }
        catch (Exception)
        {
            return false;
        }

        if (++pending.Refusals < MaxRefusals)
        {
            return false;
        }

        Drop(pending, new InvalidDataException(
            $"Storage refused capture {pending.Seq} of character {pending.Snapshot.Scalars.Id} {MaxRefusals} times while it was reachable; it is dropped."));
        return true;
    }

    // A capture that cannot be encoded can never be stored, nor journaled.
    private List<Pending> Encode(List<Pending> batch)
    {
        var encoded = new List<Pending>(batch.Count);
        foreach (var pending in batch)
        {
            try
            {
                pending.Record ??= pending.Snapshot.Encode() with { SaveSeq = pending.Seq };
                encoded.Add(pending);
            }
            catch (Exception exception)
            {
                Drop(pending, exception);
            }
        }

        return encoded;
    }

    private async Task<bool> WriteAsync(List<Pending> batch)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var written = await characters.SaveAllAsync(batch.Select(static pending => pending.Record!).ToList(), CancellationToken.None);
            Interlocked.Add(ref _written, written);
            Interlocked.Increment(ref _batches);
            Interlocked.Exchange(ref _lastBatchTicks, Stopwatch.GetTimestamp() - started);
            Complete(batch, true);
            return true;
        }
        catch (Exception exception)
        {
            Interlocked.Increment(ref _failures);
            Failed?.Invoke(exception);
            return false;
        }
    }

    private void Drop(Pending pending, Exception reason)
    {
        Interlocked.Increment(ref _dropped);
        Failed?.Invoke(reason);
        Complete([pending], false);
    }

    private void Journal(List<Pending> batch)
    {
        var encoded = Encode(batch);
        try
        {
            journal.Write(encoded.Select(static pending => pending.Record!).ToList());
            Complete(encoded, true);
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
            Complete(encoded, false);
        }
    }

    private static void Complete(List<Pending> batch, bool stored)
    {
        foreach (var pending in batch)
        {
            foreach (var waiter in pending.Waiters ?? [])
            {
                waiter.TrySetResult(stored);
            }
        }
    }
}
