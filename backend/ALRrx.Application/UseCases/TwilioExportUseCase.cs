using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;

namespace ALRrx.Application.UseCases;

public sealed class TwilioExportData
{
    public string Period { get; init; } = "today";
    public string GeneratedAt { get; init; } = ALRrx.Application.Helpers.TimeZoneHelper.NowPstString();
    public TwilioSummaryDto Summary { get; init; } = new();
    public List<TwilioDailyCostDto> Daily { get; init; } = [];
    public List<TwilioCallDto> RecentCalls { get; init; } = [];
}

public interface ITwilioPdfService
{
    string Format { get; }
    string ContentType { get; }
    byte[] GenerateTwilioPdf(TwilioExportData data);
}

public interface ITwilioExcelService
{
    string Format { get; }
    string ContentType { get; }
    byte[] GenerateTwilioExcel(TwilioExportData data);
}

public sealed class TwilioExportRequest
{
    public string Period { get; set; } = "today";
}

public sealed class TwilioExportUseCase
{
    private readonly ITwilioService _twilio;

    public TwilioExportUseCase(ITwilioService twilio)
    {
        _twilio = twilio;
    }

    public async Task<TwilioExportData> BuildDataAsync(string period, CancellationToken ct = default)
    {
        var (summary, recent, daily) = await Task.WhenAll(
            _twilio.GetSummaryAsync(period, null, null, ct),
            _twilio.GetRecentCallsAsync(50, ct),
            _twilio.GetDailyCostsAsync(30, ct)
        );

        return new TwilioExportData
        {
            Period = period,
            Summary = summary,
            Daily = daily.ToList(),
            RecentCalls = recent.ToList(),
        };
    }
}
