using System.Data.Common;

namespace ALRrx.Application.Interfaces;

public interface IDatabaseConnection : IAsyncDisposable
{
    Task<DbConnection> GetConnectionAsync(CancellationToken ct = default);
}
