using ALRrx.Application.DTOs;
using ALRrx.Domain.ValueObjects;

namespace ALRrx.Application.Helpers;

public static class TimeFilterHelper
{
    public static TimeRange BuildTimeRange(TimeFilterDto filter)
    {
        if (Enum.TryParse<Domain.Enums.TimePeriod>(filter.Period, out var period))
            return period == Domain.Enums.TimePeriod.Custom
                ? TimeRange.FromCustom(filter.CustomStart!.Value, filter.CustomEnd!.Value)
                : TimeRange.FromPeriod(period);

        return TimeRange.FromPeriod(Domain.Enums.TimePeriod.Today);
    }
}