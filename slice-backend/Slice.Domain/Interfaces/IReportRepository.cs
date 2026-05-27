using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

public interface IReportRepository
{
    Task<SliceReport?> GetByIdAsync(string reportId);
    Task SaveAsync(SliceReport report);
    Task<IReadOnlyList<SliceReport>> GetAllByEmailAsync(string email);
    Task<IReadOnlyList<SliceReport>> GetAllAsync();
}
