using System.Data.Common;

namespace PristonTale.Persistence.Abstractions;

public interface IDbConnectionFactory
{
    Task<DbConnection> OpenAsync(CancellationToken ct = default);
}
