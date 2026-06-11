using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ALRrx.Infrastructure.Twilio;

public class TwilioService : ITwilioService
{
    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly ILogger<TwilioService> _logger;
    private readonly HttpClient _httpClient;

    public TwilioService(IConfiguration configuration, ILogger<TwilioService> logger, IHttpClientFactory httpClientFactory)
    {
        _accountSid = configuration["Twilio:AccountSid"] ?? "";
        _authToken = configuration["Twilio:AuthToken"] ?? "";
        _logger = logger;

        if (string.IsNullOrEmpty(_accountSid) || string.IsNullOrEmpty(_authToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured. Set Twilio__AccountSid and Twilio__AuthToken.");
        }

        _httpClient = httpClientFactory.CreateClient();
        _httpClient.BaseAddress = new Uri("https://api.twilio.com/2010-04-01/");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<TwilioSummaryDto> GetSummaryAsync(string period, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var (start, end) = ResolvePeriod(period, startDate, endDate);
        _logger.LogInformation("[Twilio] GetSummaryAsync period='{Period}' resolved start={Start:o} end={End:o}", period, start, end);

        var calls = await FetchAllCallsAsync(start, end, ct);
        _logger.LogInformation("[Twilio] GetSummaryAsync returned {Count} calls for period '{Period}'", calls.Count, period);

        var inbound = calls.Where(c => c.Direction == "inbound").ToList();
        var outbound = calls.Where(c => c.Direction != "inbound").ToList();

        return new TwilioSummaryDto
        {
            TotalCost = calls.Sum(c => c.Cost),
            TotalCalls = calls.Count,
            TotalMinutes = (int)calls.Sum(c => c.DurationSeconds / 60.0),
            InboundCost = inbound.Sum(c => c.Cost),
            OutboundCost = outbound.Sum(c => c.Cost),
            InboundCalls = inbound.Count,
            OutboundCalls = outbound.Count,
            Currency = calls.FirstOrDefault()?.Currency ?? "USD",
            PeriodStart = start,
            PeriodEnd = end,
            LastUpdated = DateTime.UtcNow
        };
    }

    public async Task<List<TwilioCallDto>> GetRecentCallsAsync(int limit = 50, CancellationToken ct = default)
    {
        var calls = await FetchAllCallsAsync(DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, ct, limit);
        return calls
            .OrderByDescending(c => c.StartTime)
            .Select(c => new TwilioCallDto
            {
                Sid = c.Sid,
                From = c.From,
                To = c.To,
                Status = c.Status,
                Direction = c.Direction,
                DurationSeconds = c.DurationSeconds,
                Cost = c.Cost,
                Currency = c.Currency,
                StartTime = c.StartTime,
                EndTime = c.EndTime,
                HasRecording = c.HasRecording
            })
            .ToList();
    }

    public async Task<List<TwilioDailyCostDto>> GetDailyCostsAsync(int days = 30, CancellationToken ct = default)
    {
        var start = DateTime.UtcNow.AddDays(-days);
        var end = DateTime.UtcNow;

        var calls = await FetchAllCallsAsync(start, end, ct);

        return calls
            .GroupBy(c => c.StartTime.Date)
            .Select(g => new TwilioDailyCostDto
            {
                Date = g.Key,
                Cost = g.Sum(c => c.Cost),
                CallCount = g.Count(),
                Minutes = (int)g.Sum(c => c.DurationSeconds / 60.0)
            })
            .OrderBy(d => d.Date)
            .ToList();
    }

    /// <summary>
    /// Fetch ALL calls in a date range using direct HTTP calls to the
    /// Twilio REST API with proper StartTime>/StartTime&lt; filters and
    /// manual pagination via next_page_uri.
    ///
    /// The Twilio C# SDK's CallResource.ReadAsync was ignoring the
    /// date range filters and returning the first 1000 calls regardless
    /// of the period. This bypasses the SDK entirely.
    /// </summary>
    private async Task<List<TwilioCallDto>> FetchAllCallsAsync(
        DateTime start, DateTime end, CancellationToken ct, int? maxRecords = null)
    {
        var all = new List<TwilioCallDto>();
        string? nextPageUri = null;
        var page = 0;
        const int pageSize = 500;

        do
        {
            string query;
            if (nextPageUri != null)
            {
                // next_page_uri is relative like "/2010-04-01/.../Calls.json?Page=2&PageSize=1000"
                query = nextPageUri.TrimStart('/');
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append($"Accounts/{_accountSid}/Calls.json?PageSize={pageSize}");
                if (start > DateTime.MinValue)
                    sb.Append($"&StartTime={Uri.EscapeDataString(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                if (end > DateTime.MinValue && end < DateTime.UtcNow.AddYears(1))
                    sb.Append($"&EndTime={Uri.EscapeDataString(end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                query = sb.ToString();
            }

            var request = new HttpRequestMessage(HttpMethod.Get, query);
            var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);

            _logger.LogInformation("[Twilio] HTTP GET page {Page}: {Query}", page, query);
            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            // Log first 500 chars of body for debugging
            var bodyPreview = body.Length > 500 ? body.Substring(0, 500) + "..." : body;
            _logger.LogInformation("[Twilio] HTTP response body (page {Page}): {Body}", page, bodyPreview);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[Twilio] HTTP {Status}: {Body}", response.StatusCode, body);
                throw new HttpRequestException($"Twilio API returned {response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var callsArray = doc.RootElement.GetProperty("calls");
            nextPageUri = doc.RootElement.TryGetProperty("next_page_uri", out var npt) ? npt.GetString() : null;

            foreach (var callEl in callsArray.EnumerateArray())
            {
                var call = ParseCallFromJson(callEl);
                if (call != null) all.Add(call);
            }

            page++;
            _logger.LogInformation("[Twilio] Page {Page} returned {Count} calls, hasNext={HasNext}",
                page, callsArray.GetArrayLength(), nextPageUri != null);

            if (maxRecords.HasValue && all.Count >= maxRecords.Value) break;
        }
        while (!string.IsNullOrEmpty(nextPageUri));

        if (maxRecords.HasValue && all.Count > maxRecords.Value)
            all = all.Take(maxRecords.Value).ToList();

        return all;
    }

    private static TwilioCallDto? ParseCallFromJson(JsonElement el)
    {
        try
        {
            var direction = el.TryGetProperty("direction", out var dir) ? dir.GetString() ?? "" : "";
            return new TwilioCallDto
            {
                Sid = el.TryGetProperty("sid", out var sid) ? sid.GetString() ?? "" : "",
                From = el.TryGetProperty("from_formatted", out var ff) ? ff.GetString() ?? "" :
                       el.TryGetProperty("from", out var fr) ? fr.GetString() ?? "" : "",
                To = el.TryGetProperty("to_formatted", out var tf) ? tf.GetString() ?? "" :
                     el.TryGetProperty("to", out var t) ? t.GetString() ?? "" : "",
                Status = el.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
                Direction = IsInboundFromString(direction),
                DurationSeconds = el.TryGetProperty("duration", out var dur) ? ParseDuration(dur.GetString()) : 0,
                Cost = el.TryGetProperty("price", out var price) ? ParseCost(price.GetString()) : 0m,
                Currency = el.TryGetProperty("price_unit", out var pu) ? pu.GetString() ?? "USD" : "USD",
                StartTime = el.TryGetProperty("start_time", out var st) && DateTime.TryParse(st.GetString(), out var stp)
                    ? DateTime.SpecifyKind(stp, DateTimeKind.Utc)
                    : DateTime.MinValue,
                EndTime = el.TryGetProperty("end_time", out var et) && DateTime.TryParse(et.GetString(), out var etp)
                    ? (DateTime?)DateTime.SpecifyKind(etp, DateTimeKind.Utc) : null,
                HasRecording = false
            };
        }
        catch
        {
            return null;
        }
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

    private static string IsInboundFromString(string direction)
    {
        if (string.IsNullOrEmpty(direction)) return "outbound";
        return direction.ToLowerInvariant().Contains("inbound") ? "inbound" : "outbound";
    }

    private static decimal ParseCost(string? price)
    {
        return decimal.TryParse(price, System.Globalization.CultureInfo.InvariantCulture, out var p) ? Math.Abs(p) : 0m;
    }

    private static int ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 0;
        var clean = duration.Split('.')[0];
        return int.TryParse(clean, out var s) ? s : 0;
    }
}
