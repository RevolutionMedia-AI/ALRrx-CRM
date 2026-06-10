using Slice.Domain.Entities;

namespace Slice.Domain.Interfaces;

/// <summary>
/// Provides persistence for merged Slice reports.
/// Implementations may be in-memory or database-backed without changing callers.
/// </summary>
public interface IReportRepository
{
    /// <summary>
    /// Returns the report with the given ID, or <c>null</c> if not found.
    /// Implementations should NOT hydrate child collections here — the default
    /// contract is "shallow" so the cheap read path doesn't allocate large
    /// child lists. Use <see cref="GetWithChildrenAsync"/> when the full data
    /// is actually needed.
    /// </summary>
    Task<SliceReport?> GetByIdAsync(string reportId);

    /// <summary>
    /// Returns the report with all 4 child collections populated. Use only
    /// from code paths that actually need the rows (charts, edits, full exports).
    /// </summary>
    Task<SliceReport?> GetWithChildrenAsync(string reportId);

    /// <summary>Inserts or replaces a report in the store.</summary>
    Task SaveAsync(SliceReport report);

    // ── Summary projections (bust-18) ─────────────────────────────────────
    //
    // The four "Summary" methods below return header + counts (+ the
    // DailyGlobal rows for the period queries that need per-pod breakdown).
    // NO DailyAgents rows, NO ShopDaily rows, NO ShopCallMetrics rows are
    // hydrated. This replaces the old Include × 4 pattern that caused 15s
    // timeouts on the "Reports by Period" page whenever a daily query
    // returned more than a handful of reports (each with thousands of
    // ShopCallMetrics rows). Use these for the list/period endpoints and the
    // dropdown selectors. For drill-down to a single report use
    // <see cref="GetWithChildrenAsync"/>.

    /// <summary>Returns report summaries, newest first. <paramref name="emailFilter"/> narrows by owner when provided.</summary>
    Task<IReadOnlyList<ReportSummaryWithCounts>> GetAllSummariesAsync(string? emailFilter, int limit, int offset);

    /// <summary>Returns report summaries whose <c>ReportDate</c> falls on the given UTC day. Includes <c>DailyGlobal</c> rows for per-pod breakdown.</summary>
    Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateSummaryAsync(DateOnly date, string? podFilter, int limit, int offset);

    /// <summary>Returns report summaries whose <c>ReportDate</c> falls within the inclusive range. Includes <c>DailyGlobal</c> rows.</summary>
    Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateRangeSummaryAsync(DateOnly start, DateOnly end, string? podFilter, int limit, int offset);

    /// <summary>Returns report summaries whose <c>ReportDate</c> falls within the given month. Includes <c>DailyGlobal</c> rows.</summary>
    Task<IReadOnlyList<ReportSummaryWithCounts>> GetByMonthSummaryAsync(int year, int month, string? podFilter, int limit, int offset);

    /// <summary>Returns the total count of reports matching the same filters (without pagination).</summary>
    Task<int> CountByDateAsync(DateOnly date, string? podFilter = null);
    Task<int> CountByDateRangeAsync(DateOnly start, DateOnly end, string? podFilter = null);
    Task<int> CountByMonthAsync(int year, int month, string? podFilter = null);
    Task<int> CountAllAsync(string? emailFilter = null);
}
