namespace ALRrx.Application.Helpers;

/// <summary>
/// Converts UTC DateTimes to Pacific Standard Time (PST/PDT depending on DST).
/// Uses the IANA "America/Los_Angeles" timezone so DST transitions are handled
/// automatically (winter = UTC-8, summer = UTC-7).
/// </summary>
public static class TimeZoneHelper
{
    private static readonly TimeZoneInfo PacificTz =
        TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");

    public static DateTime ToPst(DateTime utc)
    {
        var utcDate = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc)
        };
        return TimeZoneInfo.ConvertTimeFromUtc(utcDate, PacificTz);
    }

    public static string ToPstString(DateTime utc, string format = "yyyy-MM-dd HH:mm:ss")
    {
        return ToPst(utc).ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string NowPstString(string format = "yyyy-MM-dd HH:mm:ss")
    {
        return ToPstString(DateTime.UtcNow, format);
    }

    public const string Label = "PST";
}
