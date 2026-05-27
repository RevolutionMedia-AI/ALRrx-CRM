using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly ConcurrentDictionary<string, SliceReport> _store = new();

    public Task<SliceReport?> GetByIdAsync(string reportId)
    {
        _store.TryGetValue(reportId, out var report);
        return Task.FromResult(report);
    }

    public Task SaveAsync(SliceReport report)
    {
        _store[report.Id] = report;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SliceReport>> GetAllByEmailAsync(string email)
    {
        IReadOnlyList<SliceReport> result = _store.Values
            .Where(r => r.GeneratedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.GeneratedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<SliceReport>> GetAllAsync()
    {
        IReadOnlyList<SliceReport> result = _store.Values
            .OrderByDescending(r => r.GeneratedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
