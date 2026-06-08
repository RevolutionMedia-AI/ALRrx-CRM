using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// Thread-safe, in-memory repository for <see cref="ProcessingJob"/> entities.
/// Uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> for lock-free reads.
/// All data is lost on restart — intended for prototyping until a DB layer is added.
/// </summary>
public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<Guid, ProcessingJob> _store = new();

    /// <inheritdoc/>
    public Task<ProcessingJob?> GetByIdAsync(Guid jobId)
    {
        _store.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    /// <inheritdoc/>
    public Task SaveAsync(ProcessingJob job)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(ProcessingJob job)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProcessingJob>> GetByEmailAsync(string email)
    {
        IReadOnlyList<ProcessingJob> result = _store.Values
            .Where(j => j.CreatedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ProcessingJob>> GetAllAsync()
    {
        IReadOnlyList<ProcessingJob> result = _store.Values
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
