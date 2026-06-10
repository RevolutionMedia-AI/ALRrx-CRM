using System.Collections.Concurrent;
using System.Diagnostics;

namespace Slice.Infrastructure.Diagnostics;

/// <summary>
/// Lightweight, in-process counter store used to surface DB / cache / request
/// performance numbers via the <c>/debug/perf</c> endpoint. Intentionally
/// lock-free: all mutations are atomic on a single int/long field, and the
/// snapshot is built by reading the fields without a lock. Safe for the
/// multi-threaded ASP.NET request pipeline.
/// </summary>
public sealed class SlicePerformanceMetrics
{
    private long _efQueryCount;
    private long _efQueryTotalTicks;
    private long _efQueryMaxTicks;
    private long _jwtSignCount;
    private long _jwtSignTotalTicks;
    private long _jwtCacheHits;
    private long _jwtCacheMisses;

    private readonly ConcurrentQueue<(long ticks, string tag, DateTime at)> _slowQueries = new();
    private const int SlowQueryRetention = 50;

    public void RecordEfQuery(long elapsedTicks, string? tag = null)
    {
        Interlocked.Increment(ref _efQueryCount);
        Interlocked.Add(ref _efQueryTotalTicks, elapsedTicks);
        UpdateMax(ref _efQueryMaxTicks, elapsedTicks);
        if (elapsedTicks > Stopwatch.Frequency / 10) // > 100ms
        {
            _slowQueries.Enqueue((elapsedTicks, tag ?? "(untagged)", DateTime.UtcNow));
            while (_slowQueries.Count > SlowQueryRetention && _slowQueries.TryDequeue(out _)) { }
        }
    }

    public void RecordJwtSign(long elapsedTicks, bool cacheHit)
    {
        Interlocked.Increment(ref _jwtSignCount);
        Interlocked.Add(ref _jwtSignTotalTicks, elapsedTicks);
        if (cacheHit) Interlocked.Increment(ref _jwtCacheHits);
        else Interlocked.Increment(ref _jwtCacheMisses);
    }

    public PerfSnapshot Snapshot() => new()
    {
        EfQueryCount       = Interlocked.Read(ref _efQueryCount),
        EfQueryTotalTicks  = Interlocked.Read(ref _efQueryTotalTicks),
        EfQueryMaxTicks    = Interlocked.Read(ref _efQueryMaxTicks),
        JwtSignCount       = Interlocked.Read(ref _jwtSignCount),
        JwtSignTotalTicks  = Interlocked.Read(ref _jwtSignTotalTicks),
        JwtCacheHits       = Interlocked.Read(ref _jwtCacheHits),
        JwtCacheMisses     = Interlocked.Read(ref _jwtCacheMisses),
        SlowQueries        = _slowQueries.ToArray(),
    };

    private static void UpdateMax(ref long location, long value)
    {
        long initial, current;
        do
        {
            current = initial = Interlocked.Read(ref location);
            if (value <= current) return;
        } while (Interlocked.CompareExchange(ref location, value, initial) != initial);
    }
}

public sealed class PerfSnapshot
{
    public long EfQueryCount      { get; init; }
    public long EfQueryTotalTicks { get; init; }
    public long EfQueryMaxTicks   { get; init; }
    public long JwtSignCount      { get; init; }
    public long JwtSignTotalTicks { get; init; }
    public long JwtCacheHits      { get; init; }
    public long JwtCacheMisses    { get; init; }
    public (long ticks, string tag, DateTime at)[] SlowQueries { get; init; } = [];
}
