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

            var startDate = filter.CustomStart.Value.Date;
            var endDate = filter.CustomEnd.Value.Date.AddDays(1).AddSeconds(-1);

            return TimeRange.FromCustom(startDate, endDate);
        }

        return TimeRange.FromPeriod(period);
    }
}