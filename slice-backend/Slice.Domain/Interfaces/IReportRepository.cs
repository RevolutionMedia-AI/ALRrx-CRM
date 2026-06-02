using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

/// <summary>
/// Provides persistence for merged Slice reports.
/// Implementations may be in-memory or database-backed without changing callers.
/// </summary>
public interface IReportRepository
{
    /// <summary>Returns the report with the given ID, or <c>null</c> if not found.</summary>
    Task<SliceReport?> GetByIdAsync(string reportId);

    /// <summary>Inserts or replaces a report in the store.</summary>
    Task SaveAsync(SliceReport report);

    /// <summary>Returns reports owned by <paramref name="email"/>, newest first.</summary>
    Task<IReadOnlyList<SliceReport>> GetAllByEmailAsync(string email);

    /// <summary>Returns all reports in the store, newest first.</summary>
    Task<IReadOnlyList<SliceReport>> GetAllAsync();
}
