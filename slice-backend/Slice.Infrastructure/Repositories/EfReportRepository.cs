using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;
using Slice.Infrastructure.Persistence;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// EF Core-backed implementation of <see cref="IReportRepository"/>. Persists
/// reports and their child collections in SQLite. All read paths use
/// <c>AsNoTracking</c> (we never mutate the loaded entities in place) and the
/// <c>GetWithChildrenAsync</c> path uses <c>AsSplitQuery</c> so EF doesn't
/// issue a cartesian product across the 4 child collections.
/// </summary>
public sealed class EfReportRepository : IReportRepository
{
    private readonly SliceDbContext _db;

    public EfReportRepository(SliceDbContext db) => _db = db;

    public async Task<SliceReport?> GetByIdAsync(string reportId)
    {
        var entity = await _db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reportId);
        return entity is null ? null : ToDomainShallow(entity);
    }

    /// <summary>
    /// Loads the report with all 4 child collections populated. Use only from
    /// code paths that actually need the data (charts, edits, full exports).
    /// Uses <c>AsSplitQuery</c> so EF issues one query per child collection
    /// instead of a single giant LEFT JOIN that produces a cartesian explosion
    /// when one report has thousands of ShopCallMetrics rows.
    /// </summary>
    public async Task<SliceReport?> GetWithChildrenAsync(string reportId)
    {
        var entity = await _db.Reports
            .AsNoTracking()
            .AsSplitQuery()
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .FirstOrDefaultAsync(r => r.Id == reportId);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task SaveAsync(SliceReport report)
    {
        // Optimized replace-by-id: execute a single DELETE that cascades to
        // the 4 child collections, then INSERT the new entity. This is ~2x
        // less I/O than the previous "load with includes + Remove + Add"
        // pattern, which had to materialize all 4 child collections just to
        // tell EF about them.
        var existingId = report.Id;
        await _db.Reports
            .Where(r => r.Id == existingId)
            .ExecuteDeleteAsync();
        _db.Reports.Add(ToEntity(report));
        await _db.SaveChangesAsync();
    }

    // ── Summary projections (bust-18) ─────────────────────────────────────
    //
    // Each method uses Select projection (not Include) so that the 3 heavy
    // child collections (DailyAgents / ShopDaily / ShopCallMetrics) are never
    // hydrated. Only the DailyGlobal rows are loaded for period queries that
    // need the per-pod breakdown. Counts are computed in SQL via subqueries
    // to avoid loading rows just to count them.

    public async Task<IReadOnlyList<ReportSummaryWithCounts>> GetAllSummariesAsync(string? emailFilter, int limit, int offset)
    {
        var query = _db.Reports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(emailFilter))
        {
            var email = emailFilter.Trim();
            query = query.Where(r => r.GeneratedByEmail == email);
        }
        return await query
            .OrderByDescending(r => r.GeneratedAt)
            .Skip(offset)
            .Take(limit)
            .Select(r => new ReportSummaryWithCounts(
                r.Id,
                r.ReportDate,
                r.GeneratedAt,
                r.GeneratedByEmail,
                r.DailyGlobal.Select(g => g.Pod).Distinct().Count(),
                r.DailyAgents.Count(),
                r.ShopDaily.Count(),
                r.ShopCallMetrics.Count(),
                new List<DailyGlobalRow>()))
            .ToListAsync();
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateSummaryAsync(DateOnly date, string? podFilter, int limit, int offset)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd   = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return QueryPeriodSummariesAsync(
            r => r.ReportDate >= dayStart && r.ReportDate < dayEnd,
            podFilter,
            r => r.OrderByDescending(x => x.GeneratedAt),
            limit, offset);
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByDateRangeSummaryAsync(DateOnly start, DateOnly end, string? podFilter, int limit, int offset)
    {
        var rangeStart = start.ToDateTime(TimeOnly.MinValue);
        var rangeEnd   = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return QueryPeriodSummariesAsync(
            r => r.ReportDate >= rangeStart && r.ReportDate < rangeEnd,
            podFilter,
            r => r.OrderByDescending(x => x.ReportDate),
            limit, offset);
    }

    public Task<IReadOnlyList<ReportSummaryWithCounts>> GetByMonthSummaryAsync(int year, int month, string? podFilter, int limit, int offset)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfNext  = firstOfMonth.AddMonths(1);
        return QueryPeriodSummariesAsync(
            r => r.ReportDate >= firstOfMonth && r.ReportDate < firstOfNext,
            podFilter,
            r => r.OrderByDescending(x => x.ReportDate),
            limit, offset);
    }

    /// <summary>
    /// Shared query body for the three "period" summary methods. Previously
    /// duplicated 3 times; consolidating here makes the SQL projection easier
    /// to evolve and ensures the DailyGlobal child projection is identical
    /// across the day / range / month queries.
    /// </summary>
    private async Task<IReadOnlyList<ReportSummaryWithCounts>> QueryPeriodSummariesAsync(
        Expression<Func<SliceReportEntity, bool>> dateFilter,
        string? podFilter,
        Func<IQueryable<SliceReportEntity>, IOrderedQueryable<SliceReportEntity>> orderBy,
        int limit, int offset)
    {
        var query = _db.Reports.AsNoTracking().Where(dateFilter);
        if (!string.IsNullOrWhiteSpace(podFilter))
        {
            var pod = podFilter.Trim();
            query = query.Where(r => r.DailyGlobal.Any(g => g.Pod == pod));
        }
        return await orderBy(query)
            .Skip(offset)
            .Take(limit)
            .Select(r => new ReportSummaryWithCounts(
                r.Id,
                r.ReportDate,
                r.GeneratedAt,
                r.GeneratedByEmail,
                r.DailyGlobal.Select(g => g.Pod).Distinct().Count(),
                r.DailyAgents.Count(),
                r.ShopDaily.Count(),
                r.ShopCallMetrics.Count(),
                r.DailyGlobal.Select(g => new DailyGlobalRow
                {
                    Pod                 = g.Pod,
                    Queued              = g.Queued,
                    Handled             = g.Handled,
                    MissedCalls         = g.MissedCalls,
                    TransferredCalls    = g.TransferredCalls,
                    PctQueued           = g.PctQueued,
                    PctHandled          = g.PctHandled,
                    PctMissed           = g.PctMissed,
                    PctTransferred      = g.PctTransferred,
                    ConvPct             = g.ConvPct,
                    OrderCount          = g.OrderCount,
                    RefundedOrders      = g.RefundedOrders,
                    PctOrdersWithErrors = g.PctOrdersWithErrors,
                }).ToList()))
            .ToListAsync();
    }

    public Task<int> CountByDateAsync(DateOnly date, string? podFilter = null)
        => CountInternalAsync(r => r.ReportDate >= date.ToDateTime(TimeOnly.MinValue) && r.ReportDate < date.AddDays(1).ToDateTime(TimeOnly.MinValue), podFilter);

    public Task<int> CountByDateRangeAsync(DateOnly start, DateOnly end, string? podFilter = null)
        => CountInternalAsync(r => r.ReportDate >= start.ToDateTime(TimeOnly.MinValue) && r.ReportDate < end.AddDays(1).ToDateTime(TimeOnly.MinValue), podFilter);

    public Task<int> CountByMonthAsync(int year, int month, string? podFilter = null)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfNext = firstOfMonth.AddMonths(1);
        return CountInternalAsync(r => r.ReportDate >= firstOfMonth && r.ReportDate < firstOfNext, podFilter);
    }

    public Task<int> CountAllAsync(string? emailFilter = null)
    {
        var q = _db.Reports.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(emailFilter))
        {
            var email = emailFilter.Trim();
            q = q.Where(r => r.GeneratedByEmail == email);
        }
        return q.CountAsync();
    }

    private Task<int> CountInternalAsync(Expression<Func<SliceReportEntity, bool>> dateFilter, string? podFilter)
    {
        var q = _db.Reports.AsNoTracking().Where(dateFilter);
        if (!string.IsNullOrWhiteSpace(podFilter))
        {
            var pod = podFilter.Trim();
            q = q.Where(r => r.DailyGlobal.Any(g => g.Pod == pod));
        }
        return q.CountAsync();
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────

    private static SliceReport ToDomainShallow(SliceReportEntity e) => new()
    {
        Id               = e.Id,
        JobId            = e.JobId,
        ReportDate       = e.ReportDate,
        GeneratedAt      = e.GeneratedAt,
        GeneratedByEmail = e.GeneratedByEmail,
        MergedCsvPath    = e.MergedCsvPath,
        MergedXlsxPath   = e.MergedXlsxPath,
    };

    private static SliceReport ToDomain(SliceReportEntity e)
    {
        var report = ToDomainShallow(e);
        // Materialize each collection once — the previous implementation
        // iterated the entity's child collections through several separate
        // Select().ToList() chains, which all walked the same ChangeTracker
        // / entity-property data. The collections are already in memory
        // because AsSplitQuery loaded them eagerly.
        if (e.DailyGlobal.Count > 0)
        {
            var dg = new List<DailyGlobalRow>(e.DailyGlobal.Count);
            foreach (var g in e.DailyGlobal)
                dg.Add(new DailyGlobalRow
                {
                    Pod                 = g.Pod,
                    Queued              = g.Queued,
                    Handled             = g.Handled,
                    MissedCalls         = g.MissedCalls,
                    TransferredCalls    = g.TransferredCalls,
                    PctQueued           = g.PctQueued,
                    PctHandled          = g.PctHandled,
                    PctMissed           = g.PctMissed,
                    PctTransferred      = g.PctTransferred,
                    ConvPct             = g.ConvPct,
                    OrderCount          = g.OrderCount,
                    RefundedOrders      = g.RefundedOrders,
                    PctOrdersWithErrors = g.PctOrdersWithErrors,
                });
            report.DailyGlobal = dg;
        }
        if (e.DailyAgents.Count > 0)
        {
            var da = new List<DailyAgentRow>(e.DailyAgents.Count);
            foreach (var a in e.DailyAgents)
                da.Add(new DailyAgentRow
                {
                    Pod                = a.Pod,
                    SupervisorName     = a.SupervisorName,
                    AgentEmail         = a.AgentEmail,
                    HC                 = a.HC,
                    TC                 = a.TC,
                    NumberOfHolds      = a.NumberOfHolds,
                    AvgHoldTime        = a.AvgHoldTime,
                    ASA                = a.ASA,
                    AHT                = a.AHT,
                    ACW                = a.ACW,
                    PctContactsOnHold  = a.PctContactsOnHold,
                    PctSLUnder15Sec    = a.PctSLUnder15Sec,
                    PctTransfers       = a.PctTransfers,
                    Shift              = a.Shift,
                });
            report.DailyAgents = da;
        }
        if (e.ShopDaily.Count > 0)
        {
            var sd = new List<ShopDailyRow>(e.ShopDaily.Count);
            foreach (var s in e.ShopDaily)
                sd.Add(new ShopDailyRow
                {
                    ShopName       = s.ShopName,
                    ShopId         = s.ShopId,
                    TotalOrders    = s.TotalOrders,
                    RefundedOrders = s.RefundedOrders,
                    ErrorRate      = s.ErrorRate,
                    ConversionRate = s.ConversionRate,
                });
            report.ShopDaily = sd;
        }
        if (e.ShopCallMetrics.Count > 0)
        {
            var sc = new List<ShopCallMetricsRow>(e.ShopCallMetrics.Count);
            foreach (var m in e.ShopCallMetrics)
                sc.Add(new ShopCallMetricsRow
                {
                    WeekStart         = m.WeekStart,
                    ShopId            = m.ShopId,
                    ShopName          = m.ShopName,
                    PodId             = m.PodId,
                    TotalCalls        = m.TotalCalls,
                    OverflowCalls     = m.OverflowCalls,
                    QueueCalls        = m.QueueCalls,
                    HandledCalls      = m.HandledCalls,
                    MissedCalls       = m.MissedCalls,
                    TransferredCalls  = m.TransferredCalls,
                    PctOverflow       = m.PctOverflow,
                    PctQueued         = m.PctQueued,
                    PctHandled        = m.PctHandled,
                    PctMissedOfQueued = m.PctMissedOfQueued,
                    PctTransferred    = m.PctTransferred,
                });
            report.ShopCallMetrics = sc;
        }
        return report;
    }

    private static SliceReportEntity ToEntity(SliceReport r)
    {
        var entity = new SliceReportEntity
        {
            Id               = r.Id,
            JobId            = r.JobId,
            ReportDate       = r.ReportDate,
            GeneratedAt      = r.GeneratedAt,
            GeneratedByEmail = r.GeneratedByEmail,
            MergedCsvPath    = r.MergedCsvPath,
            MergedXlsxPath   = r.MergedXlsxPath,
        };
        if (r.DailyGlobal.Count > 0)
        {
            var dg = new List<DailyGlobalEntity>(r.DailyGlobal.Count);
            foreach (var g in r.DailyGlobal)
                dg.Add(new DailyGlobalEntity
                {
                    Pod                 = g.Pod,
                    Queued              = g.Queued,
                    Handled             = g.Handled,
                    MissedCalls         = g.MissedCalls,
                    TransferredCalls    = g.TransferredCalls,
                    PctQueued           = g.PctQueued,
                    PctHandled          = g.PctHandled,
                    PctMissed           = g.PctMissed,
                    PctTransferred      = g.PctTransferred,
                    ConvPct             = g.ConvPct,
                    OrderCount          = g.OrderCount,
                    RefundedOrders      = g.RefundedOrders,
                    PctOrdersWithErrors = g.PctOrdersWithErrors,
                });
            entity.DailyGlobal = dg;
        }
        if (r.DailyAgents.Count > 0)
        {
            var da = new List<DailyAgentEntity>(r.DailyAgents.Count);
            foreach (var a in r.DailyAgents)
                da.Add(new DailyAgentEntity
                {
                    Pod                = a.Pod,
                    SupervisorName     = a.SupervisorName,
                    AgentEmail         = a.AgentEmail,
                    HC                 = a.HC,
                    TC                 = a.TC,
                    NumberOfHolds      = a.NumberOfHolds,
                    AvgHoldTime        = a.AvgHoldTime,
                    ASA                = a.ASA,
                    AHT                = a.AHT,
                    ACW                = a.ACW,
                    PctContactsOnHold  = a.PctContactsOnHold,
                    PctSLUnder15Sec    = a.PctSLUnder15Sec,
                    PctTransfers       = a.PctTransfers,
                    Shift              = a.Shift,
                });
            entity.DailyAgents = da;
        }
        if (r.ShopDaily.Count > 0)
        {
            var sd = new List<ShopDailyEntity>(r.ShopDaily.Count);
            foreach (var s in r.ShopDaily)
                sd.Add(new ShopDailyEntity
                {
                    ShopName       = s.ShopName,
                    ShopId         = s.ShopId,
                    TotalOrders    = s.TotalOrders,
                    RefundedOrders = s.RefundedOrders,
                    ErrorRate      = s.ErrorRate,
                    ConversionRate = s.ConversionRate,
                });
            entity.ShopDaily = sd;
        }
        if (r.ShopCallMetrics.Count > 0)
        {
            var sc = new List<ShopCallMetricsEntity>(r.ShopCallMetrics.Count);
            foreach (var m in r.ShopCallMetrics)
                sc.Add(new ShopCallMetricsEntity
                {
                    WeekStart         = m.WeekStart,
                    ShopId            = m.ShopId,
                    ShopName          = m.ShopName,
                    PodId             = m.PodId,
                    TotalCalls        = m.TotalCalls,
                    OverflowCalls     = m.OverflowCalls,
                    QueueCalls        = m.QueueCalls,
                    HandledCalls      = m.HandledCalls,
                    MissedCalls       = m.MissedCalls,
                    TransferredCalls  = m.TransferredCalls,
                    PctOverflow       = m.PctOverflow,
                    PctQueued         = m.PctQueued,
                    PctHandled        = m.PctHandled,
                    PctMissedOfQueued = m.PctMissedOfQueued,
                    PctTransferred    = m.PctTransferred,
                });
            entity.ShopCallMetrics = sc;
        }
        return entity;
    }
}
