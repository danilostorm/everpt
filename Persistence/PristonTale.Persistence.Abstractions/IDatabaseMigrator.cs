namespace PristonTale.Persistence.Abstractions;

public interface IDatabaseMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
