using Microsoft.EntityFrameworkCore;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;
using Slice.Infrastructure.Persistence;

namespace Slice.Infrastructure.Repositories;

/// <summary>
/// EF Core-backed implementation of <see cref="IReportRepository"/>. Persists
/// reports and their child collections in SQLite. Uses <c>AsNoTracking</c> for
/// read-only queries (we never mutate the loaded entities in place).
/// </summary>
public sealed class EfReportRepository : IReportRepository
{
    private readonly SliceDbContext _db;

    public EfReportRepository(SliceDbContext db) => _db = db;

    public async Task<SliceReport?> GetByIdAsync(string reportId)
    {
        // Lightweight fetch: only the scalar columns, no child collections.
        // The child collections are loaded lazily by callers that need them
        // via the explicit GetWithChildrenAsync method below. This avoids
        // hydrating thousands of ShopCallMetrics rows on every read.
        var entity = await _db.Reports
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reportId);
        if (entity is null) return null;
        return ToDomainShallow(entity);
    }

    /// <summary>
    /// Loads the report with all 4 child collections populated. Use only from
    /// code paths that actually need the data (charts, edits, full exports).
    /// </summary>
    public async Task<SliceReport?> GetWithChildrenAsync(string reportId)
    {
        var entity = await _db.Reports
            .AsNoTracking()
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .FirstOrDefaultAsync(r => r.Id == reportId);
        return entity is null ? null : ToDomain(entity);
    }

    public async Task SaveAsync(SliceReport report)
    {
        // Replace-by-id strategy: delete old entity (and cascades) then insert.
        // This keeps the mapping trivial and avoids change-tracking complexity
        // for the child collections.
        var existing = await _db.Reports
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .FirstOrDefaultAsync(r => r.Id == report.Id);
        if (existing is not null)
        {
            _db.Reports.Remove(existing);
        }
        _db.Reports.Add(ToEntity(report));
        await _db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<SliceReport>> GetAllByEmailAsync(string email)
    {
        var entities = await _db.Reports
            .AsNoTracking()
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .Where(r => r.GeneratedByEmail == email)
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync();
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SliceReport>> GetAllAsync()
    {
        var entities = await _db.Reports
            .AsNoTracking()
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync();
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SliceReport>> GetByDateAsync(DateOnly date, string? podFilter = null)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = _db.Reports
            .AsNoTracking()
            .Where(r => r.ReportDate >= dayStart && r.ReportDate < dayEnd);
        if (!string.IsNullOrWhiteSpace(podFilter))
        {
            var pod = podFilter.Trim();
            query = query.Where(r => r.DailyGlobal.Any(g => g.Pod == pod));
        }
        var entities = await query
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync();
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SliceReport>> GetByDateRangeAsync(DateOnly start, DateOnly end, string? podFilter = null)
    {
        var rangeStart = start.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var query = _db.Reports
            .AsNoTracking()
            .Where(r => r.ReportDate >= rangeStart && r.ReportDate < rangeEnd);
        if (!string.IsNullOrWhiteSpace(podFilter))
        {
            var pod = podFilter.Trim();
            query = query.Where(r => r.DailyGlobal.Any(g => g.Pod == pod));
        }
        var entities = await query
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .OrderByDescending(r => r.ReportDate)
            .ToListAsync();
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<SliceReport>> GetByMonthAsync(int year, int month, string? podFilter = null)
    {
        var firstOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var firstOfNext = firstOfMonth.AddMonths(1);
        var query = _db.Reports
            .AsNoTracking()
            .Where(r => r.ReportDate >= firstOfMonth && r.ReportDate < firstOfNext);
        if (!string.IsNullOrWhiteSpace(podFilter))
        {
            var pod = podFilter.Trim();
            query = query.Where(r => r.DailyGlobal.Any(g => g.Pod == pod));
        }
        var entities = await query
            .Include(r => r.DailyGlobal)
            .Include(r => r.DailyAgents)
            .Include(r => r.ShopDaily)
            .Include(r => r.ShopCallMetrics)
            .OrderByDescending(r => r.ReportDate)
            .ToListAsync();
        return entities.Select(ToDomain).ToList();
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

    private static SliceReport ToDomain(SliceReportEntity e) => new()
    {
        Id = e.Id,
        JobId = e.JobId,
        ReportDate = e.ReportDate,
        GeneratedAt = e.GeneratedAt,
        GeneratedByEmail = e.GeneratedByEmail,
        MergedCsvPath = e.MergedCsvPath,
        MergedXlsxPath = e.MergedXlsxPath,
        DailyGlobal = e.DailyGlobal.Select(g => new DailyGlobalRow
        {
            Pod = g.Pod,
            Queued = g.Queued,
            Handled = g.Handled,
            MissedCalls = g.MissedCalls,
            TransferredCalls = g.TransferredCalls,
            PctQueued = g.PctQueued,
            PctHandled = g.PctHandled,
            PctMissed = g.PctMissed,
            PctTransferred = g.PctTransferred,
            ConvPct = g.ConvPct,
            OrderCount = g.OrderCount,
            RefundedOrders = g.RefundedOrders,
            PctOrdersWithErrors = g.PctOrdersWithErrors,
        }).ToList(),
        DailyAgents = e.DailyAgents.Select(a => new DailyAgentRow
        {
            Pod = a.Pod,
            SupervisorName = a.SupervisorName,
            AgentEmail = a.AgentEmail,
            HC = a.HC,
            TC = a.TC,
            NumberOfHolds = a.NumberOfHolds,
            AvgHoldTime = a.AvgHoldTime,
            ASA = a.ASA,
            AHT = a.AHT,
            ACW = a.ACW,
            PctContactsOnHold = a.PctContactsOnHold,
            PctSLUnder15Sec = a.PctSLUnder15Sec,
            PctTransfers = a.PctTransfers,
            Shift = a.Shift,
        }).ToList(),
        ShopDaily = e.ShopDaily.Select(s => new ShopDailyRow
        {
            ShopName = s.ShopName,
            ShopId = s.ShopId,
            TotalOrders = s.TotalOrders,
            RefundedOrders = s.RefundedOrders,
            ErrorRate = s.ErrorRate,
            ConversionRate = s.ConversionRate,
        }).ToList(),
        ShopCallMetrics = e.ShopCallMetrics.Select(m => new ShopCallMetricsRow
        {
            WeekStart = m.WeekStart,
            ShopId = m.ShopId,
            ShopName = m.ShopName,
            PodId = m.PodId,
            TotalCalls = m.TotalCalls,
            OverflowCalls = m.OverflowCalls,
            QueueCalls = m.QueueCalls,
            HandledCalls = m.HandledCalls,
            MissedCalls = m.MissedCalls,
            TransferredCalls = m.TransferredCalls,
            PctOverflow = m.PctOverflow,
            PctQueued = m.PctQueued,
            PctHandled = m.PctHandled,
            PctMissedOfQueued = m.PctMissedOfQueued,
            PctTransferred = m.PctTransferred,
        }).ToList(),
    };

    private static SliceReportEntity ToEntity(SliceReport r) => new()
    {
        Id = r.Id,
        JobId = r.JobId,
        ReportDate = r.ReportDate,
        GeneratedAt = r.GeneratedAt,
        GeneratedByEmail = r.GeneratedByEmail,
        MergedCsvPath = r.MergedCsvPath,
        MergedXlsxPath = r.MergedXlsxPath,
        DailyGlobal = r.DailyGlobal.Select(g => new DailyGlobalEntity
        {
            Pod = g.Pod,
            Queued = g.Queued,
            Handled = g.Handled,
            MissedCalls = g.MissedCalls,
            TransferredCalls = g.TransferredCalls,
            PctQueued = g.PctQueued,
            PctHandled = g.PctHandled,
            PctMissed = g.PctMissed,
            PctTransferred = g.PctTransferred,
            ConvPct = g.ConvPct,
            OrderCount = g.OrderCount,
            RefundedOrders = g.RefundedOrders,
            PctOrdersWithErrors = g.PctOrdersWithErrors,
        }).ToList(),
        DailyAgents = r.DailyAgents.Select(a => new DailyAgentEntity
        {
            Pod = a.Pod,
            SupervisorName = a.SupervisorName,
            AgentEmail = a.AgentEmail,
            HC = a.HC,
            TC = a.TC,
            NumberOfHolds = a.NumberOfHolds,
            AvgHoldTime = a.AvgHoldTime,
            ASA = a.ASA,
            AHT = a.AHT,
            ACW = a.ACW,
            PctContactsOnHold = a.PctContactsOnHold,
            PctSLUnder15Sec = a.PctSLUnder15Sec,
            PctTransfers = a.PctTransfers,
            Shift = a.Shift,
        }).ToList(),
        ShopDaily = r.ShopDaily.Select(s => new ShopDailyEntity
        {
            ShopName = s.ShopName,
            ShopId = s.ShopId,
            TotalOrders = s.TotalOrders,
            RefundedOrders = s.RefundedOrders,
            ErrorRate = s.ErrorRate,
            ConversionRate = s.ConversionRate,
        }).ToList(),
        ShopCallMetrics = r.ShopCallMetrics.Select(m => new ShopCallMetricsEntity
        {
            WeekStart = m.WeekStart,
            ShopId = m.ShopId,
            ShopName = m.ShopName,
            PodId = m.PodId,
            TotalCalls = m.TotalCalls,
            OverflowCalls = m.OverflowCalls,
            QueueCalls = m.QueueCalls,
            HandledCalls = m.HandledCalls,
            MissedCalls = m.MissedCalls,
            TransferredCalls = m.TransferredCalls,
            PctOverflow = m.PctOverflow,
            PctQueued = m.PctQueued,
            PctHandled = m.PctHandled,
            PctMissedOfQueued = m.PctMissedOfQueued,
            PctTransferred = m.PctTransferred,
        }).ToList(),
    };
}
