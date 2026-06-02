using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// Thread-safe, in-memory repository for <see cref="SliceReport"/> entities.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads.
/// All data is lost on restart — intended for prototyping until a DB layer is added.
/// </summary>
public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly ConcurrentDictionary<string, SliceReport> _store = new();

    /// <inheritdoc/>
    public Task<SliceReport?> GetByIdAsync(string reportId)
    {
        _store.TryGetValue(reportId, out var report);
        return Task.FromResult(report);
    }

    /// <inheritdoc/>
    public Task SaveAsync(SliceReport report)
    {
        _store[report.Id] = report;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SliceReport>> GetAllByEmailAsync(string email)
    {
        IReadOnlyList<SliceReport> result = _store.Values
            .Where(r => r.GeneratedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.GeneratedAt)
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<SliceReport>> GetAllAsync()
    {
        IReadOnlyList<SliceReport> result = _store.Values
            .OrderByDescending(r => r.GeneratedAt)
            .ToList();

        return Task.FromResult(result);
    }
}
