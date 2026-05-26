using ALRrx.Domain.Enums;

namespace ALRrx.Domain.ValueObjects;

public readonly record struct TimeRange
{
    public DateTime Start { get; }
    public DateTime End { get; }

    public TimeRange(DateTime start, DateTime end)
    {
        if (start >= end)
            throw new ArgumentException("Start must be before End");

        Start = start;
        End = end;
    }

    public static TimeRange FromPeriod(TimePeriod period)
    {
        var now = DateTime.UtcNow;
        return period switch
        {
            TimePeriod.LastHour => new TimeRange(now.AddHours(-1), now),
            TimePeriod.Today => new TimeRange(now.Date, now),
            TimePeriod.ThisWeek => new TimeRange(now.Date.AddDays(-(int)now.DayOfWeek), now),
            TimePeriod.ThisMonth => new TimeRange(new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    public static TimeRange FromCustom(DateTime start, DateTime end)
    {
        var startUtc = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(end.Year, end.Month, end.Day, 23, 59, 59, DateTimeKind.Utc);
        return new TimeRange(startUtc, endUtc);
    }
}
