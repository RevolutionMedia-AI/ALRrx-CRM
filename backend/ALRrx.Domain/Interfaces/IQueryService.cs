using ALRrx.Domain.Entities;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Domain.Interfaces;

public interface IQueryService
{
    Task<ReportResult> ExecuteQueryAsync(string queryId, TimeRange timeRange, CancellationToken ct = default);
    IReadOnlyCollection<QueryDefinition> GetAvailableQueries();
}
