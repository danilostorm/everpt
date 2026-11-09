using System.Threading.Channels;
using PristonTale.Application.Sessions;

namespace PristonTale.Server.World;

// Runs the work the world tick queues for sessions (quest upkeep, starter gifts) off the tick, a few sessions at a time;
// a failure is logged and the next session goes on. At stop it waits for the work in progress.
internal static class SessionWork
{
    private const int Parallelism = 8;

    public static async Task RunAsync(ChannelReader<PlayerSession> requests, Func<PlayerSession, CancellationToken, Task> work, ILogger logger,
        string what, CancellationToken stoppingToken)
    {
        using var slots = new SemaphoreSlim(Parallelism);
        try
        {
            await foreach (var session in requests.ReadAllAsync(stoppingToken))
            {
                await slots.WaitAsync(stoppingToken);
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await work(session, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "{Work} failed for {Character}", what, session.CharacterName);
                    }
                    finally
                    {
                        slots.Release();
                    }
                }, CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        for (var slot = 0; slot < Parallelism; slot++)
        {
            await slots.WaitAsync(CancellationToken.None);
        }
    }
}
