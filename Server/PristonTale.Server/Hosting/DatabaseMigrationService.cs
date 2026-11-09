using PristonTale.Persistence.Abstractions;

namespace PristonTale.Server.Hosting;

public sealed class DatabaseMigrationService(IDatabaseMigrator migrator)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await migrator.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
