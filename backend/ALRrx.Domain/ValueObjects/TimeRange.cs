using ALRrx.Domain.Enums;

namespace ALRrx.Domain.ValueObjects;

public readonly record struct TimeRange
{
    /// <summary>
    /// Business timezone for day-based reporting. All "Today" / "ThisWeek" /
    /// "ThisMonth" ranges are computed against this zone so the report always
    /// covers a full business day regardless of where the server runs.
    /// </summary>
    public const string BusinessTimeZoneId = "America/Tijuana";

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
        var tz = ResolveBusinessTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        var todayStart = nowLocal.Date;
        var todayEnd = todayStart.AddDays(1).AddSeconds(-1);
        return period switch
        {
            TimePeriod.LastHour => new TimeRange(nowLocal.AddHours(-1), nowLocal),
            TimePeriod.Today => new TimeRange(todayStart, todayEnd),
            TimePeriod.ThisWeek => new TimeRange(
                todayStart.AddDays(-(int)todayStart.DayOfWeek),
                todayEnd),
            TimePeriod.ThisMonth => new TimeRange(
                new DateTime(todayStart.Year, todayStart.Month, 1, 0, 0, 0),
                todayEnd),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    public static TimeRange FromCustom(DateTime start, DateTime end)
    {
        // Custom dates are interpreted in the business timezone (America/Tijuana).
        // The caller typically passes dates picked from a date picker in the UI;
        // they are wall-clock dates for the business, not UTC instants.
        var startDate = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0);
        var endDate = new DateTime(end.Year, end.Month, end.Day, 23, 59, 59);
        return new TimeRange(startDate, endDate);
    }

    private static TimeZoneInfo ResolveBusinessTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback for hosts that don't ship the IANA tz database (some Linux containers).
            // UTC-7 covers the entire year for Tijuana (no DST in MX since 2022).
            return TimeZoneInfo.CreateCustomTimeZone(
                id: "Tijuana-Offset",
                baseUtcOffset: TimeSpan.FromHours(-7),
                displayName: "America/Tijuana (UTC-7 fixed)",
                standardDisplayName: "America/Tijuana");
        }
    }
}
