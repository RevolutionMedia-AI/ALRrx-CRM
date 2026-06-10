using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Slice.Infrastructure.Diagnostics;

namespace Slice.Infrastructure.Persistence;

/// <summary>
/// EF Core interceptor that records per-query wall time into the
/// <see cref="SlicePerformanceMetrics"/> counter store. Tagged with the SQL
/// verb (SELECT/INSERT/UPDATE/DELETE) so the /debug/perf endpoint can break
/// down the slow-query log by operation type.
/// </summary>
public sealed class EfQueryMetricsInterceptor : DbCommandInterceptor
{
    private readonly SlicePerformanceMetrics _metrics;

    public EfQueryMetricsInterceptor(SlicePerformanceMetrics metrics) => _metrics = metrics;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        command.CommandTimeout = 60;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        command.CommandTimeout = 60;
        return await base.ReaderExecutingAsync(command, eventData, result, ct);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Record(command, eventData.Duration);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return await base.ReaderExecutedAsync(command, eventData, result, ct);
    }

    public override int NonQueryExecuted(
        DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Record(command, eventData.Duration);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return await base.NonQueryExecutedAsync(command, eventData, result, ct);
    }

    public override object? ScalarExecuted(
        DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Record(command, eventData.Duration);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override async ValueTask<object?> ScalarExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken ct = default)
    {
        Record(command, eventData.Duration);
        return await base.ScalarExecutedAsync(command, eventData, result, ct);
    }

    private void Record(DbCommand cmd, TimeSpan duration)
    {
        var tag = (cmd.CommandText ?? string.Empty)
            .TrimStart()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()
            ?.ToUpperInvariant() ?? "SQL";
        _metrics.RecordEfQuery((long)(duration.TotalSeconds * Stopwatch.Frequency), tag);
    }
}
