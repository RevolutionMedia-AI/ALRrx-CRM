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
        var summaryTask = _twilio.GetSummaryAsync(period, null, null, ct);
        var recentTask = _twilio.GetRecentCallsAsync(50, ct);
        var dailyTask = _twilio.GetDailyCostsAsync(30, ct);

        await Task.WhenAll(summaryTask, recentTask, dailyTask);

        return new TwilioExportData
        {
            Period = period,
            Summary = await summaryTask,
            Daily = (await dailyTask).ToList(),
            RecentCalls = (await recentTask).ToList(),
        };
    }
}
