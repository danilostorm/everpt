using PristonTale.Application.Settings;

namespace PristonTale.Server.Hosting;

public sealed class ServerSettingsService(ServerSettingsAppService settings, ILogger<ServerSettingsService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var current = await settings.ReloadAsync(cancellationToken);
        Report(current);
        _loop = Task.Run(() => LoopAsync(_stopping.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        if (_loop is not null)
        {
            await Task.WhenAny(_loop, Task.Delay(Timeout.Infinite, cancellationToken));
        }
    }

    public void Dispose() => _stopping.Dispose();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.Current.ReloadMinutes), ct);
                Report(await settings.ReloadAsync(ct));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Could not read the server settings; the settings in force stay.");
            }
        }
    }

    private void Report(ServerSettings current)
    {
        foreach (var problem in settings.Problems)
        {
            logger.LogWarning("Server setting refused: {Problem}", problem);
        }

        logger.LogDebug("Server settings read: {Name}, experience x{Experience}, reload every {Minutes} minutes.", current.ServerName,
            current.World.ExperienceMultiplier, current.ReloadMinutes);
    }
}
