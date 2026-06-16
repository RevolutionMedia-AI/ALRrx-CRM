using ALRrx.Application.DTOs;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Application.Helpers;

public static class TimeFilterHelper
{
    public static TimeRange BuildTimeRange(TimeFilterDto filter)
    {
        if (!Enum.TryParse<Domain.Enums.TimePeriod>(filter.Period, ignoreCase: true, out var period))
            throw new ArgumentException($"Invalid period value: '{filter.Period}'");

        if (period == Domain.Enums.TimePeriod.Custom)
        {
            if (!filter.CustomStart.HasValue || !filter.CustomEnd.HasValue)
                throw new ArgumentException("CustomStart and CustomEnd are required when period is Custom");

            // Custom dates are already wall-clock dates picked by the user in
            // the dashboard; we use them as-is (no UTC conversion) so the
            // "Today" semantics are consistent with the business timezone
            // used by TimeRange.FromPeriod.
            var startDate = filter.CustomStart.Value.Date;
            var endDate = filter.CustomEnd.Value.Date;

            return TimeRange.FromCustom(startDate, endDate);
        }

        return TimeRange.FromPeriod(period);
    }
}