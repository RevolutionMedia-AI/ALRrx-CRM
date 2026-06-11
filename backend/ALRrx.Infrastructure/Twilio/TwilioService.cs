using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Twilio;
using Twilio.Rest.Api.V2010.Account;

namespace ALRrx.Infrastructure.Twilio;

public class TwilioService : ITwilioService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly ILogger<TwilioService> _logger;

    public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger)
    {
        _accountSid = configuration["Twilio:AccountSid"] ?? "";
        _authToken = configuration["Twilio:AuthToken"] ?? "";
        _logger = logger;

        if (string.IsNullOrEmpty(_accountSid) || string.IsNullOrEmpty(_authToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured. Set Twilio__AccountSid and Twilio__AuthToken.");
        }

        TwilioClient.Init(_accountSid, _authToken);
    }

    public async Task<TwilioSummaryDto> GetSummaryAsync(string period, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(period, startDate, endDate);

        var calls = await CallResource.ReadAsync(
            startTime: start,
            endTime: end,
            pageSize: 1000,
            limit: 1000
        );

        var callList = calls.ToList();
        var inbound = callList.Where(c => IsInbound(c.Direction)).ToList();
        var outbound = callList.Where(c => !IsInbound(c.Direction)).ToList();

        return new TwilioSummaryDto
        {
            TotalCost = callList.Sum(c => ParseCost(c.Price)),
            TotalCalls = callList.Count,
            TotalMinutes = (int)callList.Sum(c => ParseDuration(c.Duration) / 60.0),
            InboundCost = inbound.Sum(c => ParseCost(c.Price)),
            OutboundCost = outbound.Sum(c => ParseCost(c.Price)),
            InboundCalls = inbound.Count,
            OutboundCalls = outbound.Count,
            Currency = callList.FirstOrDefault()?.PriceUnit ?? "USD",
            PeriodStart = start,
            PeriodEnd = end,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<List<TwilioCallDto>> GetRecentCallsAsync(int limit = 50, CancellationToken ct = default)
    {
        var calls = await CallResource.ReadAsync(pageSize: limit, limit: limit);

        return calls.Select(c => new TwilioCallDto
        {
            Sid = c.Sid,
            From = c.From?.ToString() ?? "",
            To = c.To?.ToString() ?? "",
            Status = c.Status?.ToString() ?? "",
            Direction = IsInbound(c.Direction) ? "inbound" : "outbound",
            DurationSeconds = ParseDuration(c.Duration),
            Cost = ParseCost(c.Price),
            Currency = c.PriceUnit ?? "USD",
            StartTime = c.StartTime ?? DateTime.MinValue,
            EndTime = c.EndTime,
            HasRecording = false
        }).ToList();
    }

    public async Task<List<TwilioDailyCostDto>> GetDailyCostsAsync(int days = 30, CancellationToken ct = default)
    {
        var start = DateTime.UtcNow.AddDays(-days);
        var end = DateTime.UtcNow;

        var calls = await CallResource.ReadAsync(
            startTime: start,
            endTime: end,
            pageSize: 1000,
            limit: 1000
        );

        return calls
            .GroupBy(c => (c.StartTime ?? DateTime.UtcNow).Date)
            .Select(g => new TwilioDailyCostDto
            {
                Date = g.Key,
                Cost = g.Sum(c => ParseCost(c.Price)),
                CallCount = g.Count(),
                Minutes = (int)g.Sum(c => ParseDuration(c.Duration) / 60.0)
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    private static (DateTime start, DateTime end) ResolvePeriod(string period, DateTime? startDate, DateTime? endDate)
    {
        var now = DateTime.UtcNow;
        return period?.ToLowerInvariant() switch
        {
            "today" => (now.Date, now),
            "thisweek" or "week" => (now.AddDays(-(int)now.DayOfWeek).Date, now),
            "thismonth" or "month" => (new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc), now),
            "custom" when startDate.HasValue && endDate.HasValue =>
                (DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc), DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)),
            _ => (now.AddDays(-1), now)
        };
    }

    private static bool IsInbound(CallResource.DirectionEnum? dir)
    {
        var s = dir?.ToString() ?? "";
        return s == "InboundApi" || s == "Inbound" || s == "InboundCall";
    }

    private static decimal ParseCost(string? price)
    {
        return decimal.TryParse(price, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0m;
    }

    private static int ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 0;
        var clean = duration.Split('.')[0];
        return int.TryParse(clean, out var s) ? s : 0;
    }
}
