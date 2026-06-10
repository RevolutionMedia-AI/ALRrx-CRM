using System.Collections.Concurrent;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// Thread-safe, in-memory repository for <see cref="SliceReport"/> entities.
/// Uses a <c>ConcurrentDictionary&lt;string, SliceReport&gt;</c> for lock-free reads.
/// All data is lost on restart — intended for prototyping until a DB layer is added.
/// </summary>
public sealed class InMemoryReportRepository : IReportRepository
{
    private readonly ConcurrentDictionary<string, SliceReport> _store = new();

    public Task<SliceReport?> GetByIdAsync(string reportId)
    {
        _store.TryGetValue(reportId, out var report);
        return Task.FromResult(report);
    }

    public Task<SliceReport?> GetWithChildrenAsync(string reportId)
    {
        _store.TryGetValue(reportId, out var report);
        return Task.FromResult(report);
    }

    public Task SaveAsync(SliceReport report)
    {
        _store[report.Id] = report;
        return Task.CompletedTask;
    }

    // ── Summary projections (bust-18) ─────────────────────────────────────

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetAllSummariesAsync(string? emailFilter, int limit, int offset)
    {
        var query = _store.Values.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(emailFilter))
        {
            var email = emailFilter.Trim();
            query = query.Where(r => r.GeneratedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase));
        }
        IReadOnlyList<ReportSummaryWithCounts> result = query
            .OrderByDescending(r => r.GeneratedAt)
            .Skip(offset)
            .Take(limit)
            .Select(ToSummary)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateSummaryAsync(DateOnly date, string? podFilter, int limit, int offset)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        IReadOnlyList<ReportSummaryWithCounts> result = _store.Values
            .Where(r => r.ReportDate >= dayStart && r.ReportDate < dayEnd)
            .Where(r => string.IsNullOrWhiteSpace(podFilter)
                         || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.GeneratedAt)
            .Skip(offset)
            .Take(limit)
            .Select(ToSummary)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateRangeSummaryAsync(DateOnly start, DateOnly end, string? podFilter, int limit, int offset)
    {
        var rangeStart = start.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        IReadOnlyList<ReportSummaryWithCounts> result = _store.Values
            .Where(r => r.ReportDate >= rangeStart && r.ReportDate < rangeEnd)
            .Where(r => string.IsNullOrWhiteSpace(podFilter)
                         || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.ReportDate)
            .Skip(offset)
            .Take(limit)
            .Select(ToSummary)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByMonthSummaryAsync(int year, int month, string? podFilter, int limit, int offset)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfNext = firstOfMonth.AddMonths(1);
        IReadOnlyList<ReportSummaryWithCounts> result = _store.Values
            .Where(r => r.ReportDate >= firstOfMonth && r.ReportDate < firstOfNext)
            .Where(r => string.IsNullOrWhiteSpace(podFilter)
                         || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.ReportDate)
            .Skip(offset)
            .Take(limit)
            .Select(ToSummary)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> CountByDateAsync(DateOnly date, string? podFilter = null)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return Task.FromResult(_store.Values
            .Count(r => r.ReportDate >= dayStart && r.ReportDate < dayEnd
                && (string.IsNullOrWhiteSpace(podFilter)
                    || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))));
    }

    public Task<int> CountByDateRangeAsync(DateOnly start, DateOnly end, string? podFilter = null)
    {
        var rangeStart = start.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return Task.FromResult(_store.Values
            .Count(r => r.ReportDate >= rangeStart && r.ReportDate < rangeEnd
                && (string.IsNullOrWhiteSpace(podFilter)
                    || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))));
    }

    public Task<int> CountByMonthAsync(int year, int month, string? podFilter = null)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfNext = firstOfMonth.AddMonths(1);
        return Task.FromResult(_store.Values
            .Count(r => r.ReportDate >= firstOfMonth && r.ReportDate < firstOfNext
                && (string.IsNullOrWhiteSpace(podFilter)
                    || r.DailyGlobal.Any(g => g.Pod.Equals(podFilter.Trim(), StringComparison.OrdinalIgnoreCase)))));
    }

    public Task<int> CountAllAsync(string? emailFilter = null)
    {
        if (string.IsNullOrWhiteSpace(emailFilter))
            return Task.FromResult(_store.Count);
        var email = emailFilter.Trim();
        return Task.FromResult(_store.Values.Count(r => r.GeneratedByEmail.Equals(email, StringComparison.OrdinalIgnoreCase)));
    }

    private static ReportSummaryWithCounts ToSummary(SliceReport r) => new(
        r.Id,
        r.ReportDate,
        r.GeneratedAt,
        r.GeneratedByEmail,
        r.DailyGlobal.Select(g => g.Pod).Distinct().Count(),
        r.DailyAgents.Count,
        r.ShopDaily.Count,
        r.ShopCallMetrics.Count,
        r.DailyGlobal.ToList());
}
