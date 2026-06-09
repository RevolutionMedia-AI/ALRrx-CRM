using Microsoft.EntityFrameworkCore;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext for Slice. Persists reports and child collections in
/// SQLite (file-based, no external server required). The container's
/// persistent volume must be mounted at the path configured in
/// <c>Slice:Database:ConnectionString</c> so the .db file survives restarts.
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
