using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

public interface IJobRepository
{
    Task<ProcessingJob?> GetByIdAsync(Guid jobId);
    Task SaveAsync(ProcessingJob job);
    Task UpdateAsync(ProcessingJob job);
    Task<IReadOnlyList<ProcessingJob>> GetByEmailAsync(string email);
}
