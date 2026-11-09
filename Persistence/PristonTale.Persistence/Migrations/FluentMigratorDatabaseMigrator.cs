using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using PristonTale.Persistence.Abstractions;

namespace PristonTale.Persistence.Migrations;

public sealed class FluentMigratorDatabaseMigrator(IServiceProvider services) : IDatabaseMigrator
{
    public Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        // FluentMigrator's runner is synchronous; there is no async migrate API. Run it directly —
        // it is only ever invoked once at startup / seeding, off the hot path.
        cancellationToken.ThrowIfCancellationRequested();
        using var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
        return Task.CompletedTask;
    }
}
