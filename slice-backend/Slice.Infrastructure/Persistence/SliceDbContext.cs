using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for Slice. Persists reports and child collections in
/// SQLite (file-based, no external server required). The container's
/// persistent volume must be mounted at the path configured in
/// <c>Slice:Database:ConnectionString</c> so the .db file survives restarts.
/// On construction we apply a set of performance PRAGMAs (WAL, larger cache,
/// memory-mapped I/O) that are safe for a single-writer workload like ours
/// and dramatically reduce query latency on the reports list.
/// </summary>
public sealed class SliceDbContext : DbContext
{
    public SliceDbContext(DbContextOptions<SliceDbContext> options) : base(options) { }

    public DbSet<SliceReportEntity> Reports => Set<SliceReportEntity>();
    public DbSet<DailyGlobalEntity> DailyGlobal => Set<DailyGlobalEntity>();
    public DbSet<DailyAgentEntity> DailyAgents => Set<DailyAgentEntity>();
    public DbSet<ShopDailyEntity> ShopDaily => Set<ShopDailyEntity>();
    public DbSet<ShopCallMetricsEntity> ShopCallMetrics => Set<ShopCallMetricsEntity>();
    public DbSet<ProcessingJobEntity> ProcessingJobs => Set<ProcessingJobEntity>();

    /// <summary>
    /// Connects directly (bypassing EF) to apply PRAGMAs that aren't exposed
    /// by <c>UseSqlite</c>'s connection-string builder. Safe to call multiple
    /// times per process — PRAGMAs are idempotent. Returns the live PRAGMA
    /// values so the caller can surface them in the <c>/debug/perf</c> endpoint.
    /// </summary>
    public async Task<SqlitePragmaStats> ApplyPerformancePragmasAsync(CancellationToken ct = default)
    {
        var conn = (SqliteConnection)Database.GetDbConnection();
        var wasOpen = conn.State == System.Data.ConnectionState.Open;
        if (!wasOpen) await conn.OpenAsync(ct);
        try
        {
            await ExecAsync(conn, "PRAGMA journal_mode = WAL;", ct);
            await ExecAsync(conn, "PRAGMA synchronous = NORMAL;", ct);
            await ExecAsync(conn, "PRAGMA temp_store = MEMORY;", ct);
            // Negative cache_size = KB; -20000 ≈ 20 MB page cache.
            await ExecAsync(conn, "PRAGMA cache_size = -20000;", ct);
            // 256 MB memory-mapped I/O window — single-writer workloads benefit a lot.
            await ExecAsync(conn, "PRAGMA mmap_size = 268435456;", ct);
            await ExecAsync(conn, "PRAGMA foreign_keys = ON;", ct);

            var stats = new SqlitePragmaStats
            {
                JournalMode = await ScalarAsync<string>(conn, "PRAGMA journal_mode;", ct) ?? "unknown",
                Synchronous = await ScalarAsync<long>(conn, "PRAGMA synchronous;", ct),
                CacheSize   = await ScalarAsync<long>(conn, "PRAGMA cache_size;", ct),
                MmapSize    = await ScalarAsync<long>(conn, "PRAGMA mmap_size;", ct),
                TempStore   = await ScalarAsync<long>(conn, "PRAGMA temp_store;", ct),
                PageCount   = await ScalarAsync<long>(conn, "PRAGMA page_count;", ct),
                PageSize    = await ScalarAsync<long>(conn, "PRAGMA page_size;", ct),
            };
            stats.DbSizeBytes = stats.PageCount * stats.PageSize;
            return stats;
        }
        finally
        {
            if (!wasOpen) await conn.CloseAsync();
        }
    }

    private static async Task ExecAsync(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<T?> ScalarAsync<T>(SqliteConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull) return default;
        return (T)Convert.ChangeType(result, typeof(T));
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── SliceReport (parent table) ──────────────────────────────────────
        modelBuilder.Entity<SliceReportEntity>(e =>
        {
            e.ToTable("SliceReports");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.JobId).HasMaxLength(64);
            e.Property(x => x.GeneratedByEmail).HasMaxLength(320);
            e.Property(x => x.MergedCsvPath).HasMaxLength(1024);
            e.Property(x => x.MergedXlsxPath).HasMaxLength(1024);
            e.HasIndex(x => x.ReportDate);
            e.HasIndex(x => x.GeneratedByEmail);
            e.HasIndex(x => x.GeneratedAt);

            // Composite index for the "list my reports" path: filter by email
            // and sort by date. The previous single-column index forced SQLite
            // to sort in memory; the composite index satisfies both from the
            // index alone.
            e.HasIndex(x => new { x.GeneratedByEmail, x.ReportDate })
                .HasDatabaseName("IX_SliceReports_Email_ReportDate");

            // Composite for the period queries: filter by ReportDate range,
            // order by GeneratedAt DESC for the "newest first" pagination.
            e.HasIndex(x => new { x.ReportDate, x.GeneratedAt })
                .HasDatabaseName("IX_SliceReports_ReportDate_GeneratedAt");
        });

        // ── DailyGlobal (child: 1 report → many rows) ─────────────────────
        modelBuilder.Entity<DailyGlobalEntity>(e =>
        {
            e.ToTable("DailyGlobalRows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Pod).HasMaxLength(32);
            e.HasIndex(x => new { x.ReportId, x.Pod });
            e.HasOne(x => x.Report)
             .WithMany(r => r.DailyGlobal)
             .HasForeignKey(x => x.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── DailyAgent ──────────────────────────────────────────────────────
        modelBuilder.Entity<DailyAgentEntity>(e =>
        {
            e.ToTable("DailyAgentRows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Pod).HasMaxLength(32);
            e.Property(x => x.SupervisorName).HasMaxLength(256);
            e.Property(x => x.AgentEmail).HasMaxLength(320);
            e.Property(x => x.Shift).HasMaxLength(32);
            e.HasIndex(x => new { x.ReportId, x.Pod });
            e.HasOne(x => x.Report)
             .WithMany(r => r.DailyAgents)
             .HasForeignKey(x => x.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ShopDaily ───────────────────────────────────────────────────────
        modelBuilder.Entity<ShopDailyEntity>(e =>
        {
            e.ToTable("ShopDailyRows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ShopName).HasMaxLength(256);
            e.Property(x => x.ShopId).HasMaxLength(64);
            e.HasIndex(x => new { x.ReportId, x.ShopId });
            e.HasOne(x => x.Report)
             .WithMany(r => r.ShopDaily)
             .HasForeignKey(x => x.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ShopCallMetrics ───────────────────────────────────────────────
        modelBuilder.Entity<ShopCallMetricsEntity>(e =>
        {
            e.ToTable("ShopCallMetricsRows");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.ShopId).HasMaxLength(64);
            e.Property(x => x.ShopName).HasMaxLength(256);
            e.Property(x => x.PodId).HasMaxLength(32);
            e.HasIndex(x => new { x.ReportId, x.ShopId, x.PodId, x.WeekStart });
            e.HasIndex(x => x.WeekStart);
            e.HasOne(x => x.Report)
             .WithMany(r => r.ShopCallMetrics)
             .HasForeignKey(x => x.ReportId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ProcessingJob (side table; lets us track uploads) ─────────────
        modelBuilder.Entity<ProcessingJobEntity>(e =>
        {
            e.ToTable("ProcessingJobs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.ReportId).HasMaxLength(64);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.ErrorMessage).HasMaxLength(2048);
            e.Property(x => x.CreatedByEmail).HasMaxLength(320);
            e.HasIndex(x => x.CreatedByEmail);
        });
    }
}

/// <summary>
/// Snapshot of the PRAGMA values applied to the SQLite database, used by the
/// <c>/debug/perf</c> endpoint to confirm the production instance is running
/// with the optimized settings.
/// </summary>
public sealed class SqlitePragmaStats
{
    public string JournalMode { get; set; } = string.Empty;
    public long   Synchronous { get; set; }
    public long   CacheSize   { get; set; }
    public long   MmapSize    { get; set; }
    public long   TempStore   { get; set; }
    public long   PageCount   { get; set; }
    public long   PageSize    { get; set; }
    public long   DbSizeBytes { get; set; }
}
