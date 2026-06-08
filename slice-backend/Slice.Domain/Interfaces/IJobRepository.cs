using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

/// <summary>
/// Provides persistence for background file-processing jobs.
/// Implementations may be in-memory or database-backed without changing callers.
/// </summary>
public interface IJobRepository
{
    /// <summary>Returns the job with the given ID, or <c>null</c> if not found.</summary>
    Task<ProcessingJob?> GetByIdAsync(Guid jobId);

    /// <summary>Inserts or replaces a job in the store.</summary>
    Task SaveAsync(ProcessingJob job);

    /// <summary>Updates the state of an existing job (e.g. status, progress counter).</summary>
    Task UpdateAsync(ProcessingJob job);

    /// <summary>Returns all jobs created by <paramref name="email"/>, newest first.</summary>
    Task<IReadOnlyList<ProcessingJob>> GetByEmailAsync(string email);

    /// <summary>Returns every job in the store (Admin use only).</summary>
    Task<IReadOnlyList<ProcessingJob>> GetAllAsync();
}
