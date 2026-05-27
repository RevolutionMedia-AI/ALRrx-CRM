using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<Guid, ProcessingJob> _store = new();

    public Task<ProcessingJob?> GetByIdAsync(Guid jobId)
    {
        _store.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task SaveAsync(ProcessingJob job)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProcessingJob job)
    {
        _store[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProcessingJob>> GetByEmailAsync(string email)
    {
        IReadOnlyList<ProcessingJob> result = _store.Values
            .Where(j => j.CreatedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }
}
