using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ALRrx.Infrastructure.Twilio;

public class TwilioService : ITwilioService
{
    private const string TwilioApiBase = "https://api.twilio.com";
    private static readonly TimeSpan SummaryTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RecentTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DailyTtl = TimeSpan.FromSeconds(60);

    private readonly string _accountSid;
    private readonly string _authToken;
    private readonly ILogger<TwilioService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;

    public TwilioService(
        IConfiguration configuration,
        ILogger<TwilioService> logger,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _accountSid = configuration["Twilio:AccountSid"] ?? "";
        _authToken = configuration["Twilio:AuthToken"] ?? "";
        _logger = logger;
        _cache = cache;

        if (string.IsNullOrEmpty(_accountSid) || string.IsNullOrEmpty(_authToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured. Set Twilio__AccountSid and Twilio__AuthToken.");
        }

        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    private static string AbsoluteUrl(string pathOrUrl)
    {
        if (string.IsNullOrEmpty(pathOrUrl)) return TwilioApiBase;
        if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return pathOrUrl;
        return TwilioApiBase + (pathOrUrl.StartsWith("/") ? pathOrUrl : "/" + pathOrUrl);
    }

    public async Task<TwilioSummaryDto> GetSummaryAsync(string period, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default)
    {
        var key = $"twilio:summary:{period}:{startDate:o}:{endDate:o}";
        if (_cache.TryGetValue(key, out TwilioSummaryDto? cached) && cached != null)
        {
            _logger.LogInformation("[Twilio] GetSummaryAsync cache HIT for key {Key}", key);
            return cached;
        }

        var (start, end) = ResolvePeriod(period, startDate, endDate);
        _logger.LogInformation("[Twilio] GetSummaryAsync period='{Period}' resolved start={Start:o} end={End:o}", period, start, end);

        var calls = await FetchAllCallsAsync(start, end, ct);

        var inbound = calls.Where(c => c.Direction == "inbound").ToList();
        var outbound = calls.Where(c => c.Direction != "inbound").ToList();

        var pricedCalls = calls.Where(c => c.Cost.HasValue).ToList();
        var pricedInbound = pricedCalls.Where(c => c.Direction == "inbound").ToList();
        var pricedOutbound = pricedCalls.Where(c => c.Direction != "inbound").ToList();

        var totalCost = pricedCalls.Sum(c => c.Cost ?? 0m);
        var inboundCost = pricedInbound.Sum(c => c.Cost ?? 0m);
        var outboundCost = pricedOutbound.Sum(c => c.Cost ?? 0m);
        var currency = pricedCalls.FirstOrDefault(c => !string.IsNullOrEmpty(c.Currency))?.Currency ?? "USD";

        var pricedSeconds = pricedCalls.Sum(c => c.DurationSeconds);
        var totalSeconds = calls.Sum(c => c.DurationSeconds);

        _logger.LogInformation("[Twilio] GetSummaryAsync period='{Period}' calls={Total} priced={Priced} totalCost={Cost} in={In} out={Out} {Currency} pricedMinutes={PMin} totalMinutes={TMin}",
            period, calls.Count, pricedCalls.Count, totalCost, inboundCost, outboundCost, currency,
            (int)(pricedSeconds / 60.0), (int)(totalSeconds / 60.0));

        var summary = new TwilioSummaryDto
        {
            TotalCost = totalCost,
            TotalCalls = calls.Count,
            TotalMinutes = (int)(totalSeconds / 60.0),
            PricedMinutes = (int)(pricedSeconds / 60.0),
            CostPerMinute = pricedSeconds > 0 ? totalCost / ((decimal)pricedSeconds / 60m) : null,
            InboundCost = inboundCost,
            OutboundCost = outboundCost,
            InboundCalls = inbound.Count,
            OutboundCalls = outbound.Count,
            Currency = currency,
            PeriodStart = start,
            PeriodEnd = end,
            LastUpdated = DateTime.UtcNow
        };

        _cache.Set(key, summary, SummaryTtl);
        return summary;
    }

    public async Task<List<TwilioCallDto>> GetRecentCallsAsync(string period = "today", int limit = 50, CancellationToken ct = default)
    {
        var key = $"twilio:recent:{period}:{limit}";
        if (_cache.TryGetValue(key, out List<TwilioCallDto>? cached) && cached != null)
        {
            _logger.LogInformation("[Twilio] GetRecentCallsAsync cache HIT for key {Key}", key);
            return cached;
        }

        var (start, end) = ResolvePeriod(period, null, null);
        var calls = await FetchAllCallsAsync(start, end, ct, limit);
        var result = calls
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

        _cache.Set(key, result, RecentTtl);
        return result;
    }

    public async Task<List<TwilioDailyCostDto>> GetDailyCostsAsync(string period = "today", CancellationToken ct = default)
    {
        var key = $"twilio:daily:{period}";
        if (_cache.TryGetValue(key, out List<TwilioDailyCostDto>? cached) && cached != null)
        {
            _logger.LogInformation("[Twilio] GetDailyCostsAsync cache HIT for key {Key}", key);
            return cached;
        }

        var (start, end) = ResolvePeriod(period, null, null);

        var calls = await FetchAllCallsAsync(start, end, ct);

        var result = calls
            .GroupBy(c => c.StartTime.Date)
            .Select(g => new TwilioDailyCostDto
            {
                Date = g.Key,
                Cost = g.Sum(c => c.Cost ?? 0m),
                CallCount = g.Count(),
                Minutes = (int)g.Sum(c => c.DurationSeconds / 60.0)
            })
            .OrderBy(d => d.Date)
            .ToList();

        _cache.Set(key, result, DailyTtl);
        return result;
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
            string url;
            if (nextPageUri != null)
            {
                url = AbsoluteUrl(nextPageUri);
            }
            else
            {
                var sb = new StringBuilder();
                sb.Append($"/2010-04-01/Accounts/{_accountSid}/Calls.json?PageSize={pageSize}");
                if (start > DateTime.MinValue)
                    sb.Append($"&StartTime>={Uri.EscapeDataString(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                if (end > DateTime.MinValue && end < DateTime.UtcNow.AddYears(1))
                    sb.Append($"&EndTime<={Uri.EscapeDataString(end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                url = AbsoluteUrl(sb.ToString());
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);

            _logger.LogInformation("[Twilio] HTTP GET page {Page}: {Url}", page, url);
            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

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
                Cost = el.TryGetProperty("price", out var price) ? ParseNullableCost(price.GetString()) : null,
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

    private static decimal? ParseNullableCost(string? price)
    {
        if (string.IsNullOrEmpty(price)) return null;
        if (price == "null") return null;
        return decimal.TryParse(price, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var p) ? Math.Abs(p) : null;
    }

    private static (DateTime start, DateTime end) ResolvePeriod(string period, DateTime? startDate, DateTime? endDate)
    {
        var nowUtc = DateTime.UtcNow;
        var pstNow = TimeZoneHelper.ToPst(nowUtc);
        return period?.ToLowerInvariant() switch
        {
            "today" => (TimeZoneHelper.ToUtc(pstNow.Date), nowUtc),
            "thisweek" or "week" => (TimeZoneHelper.ToUtc(pstNow.AddDays(-(int)pstNow.DayOfWeek).Date), nowUtc),
            "thismonth" or "month" => (TimeZoneHelper.ToUtc(new DateTime(pstNow.Year, pstNow.Month, 1)), nowUtc),
            "custom" when startDate.HasValue && endDate.HasValue =>
                (DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc), DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)),
            _ => (nowUtc.AddDays(-1), nowUtc)
        };
    }

    private static string IsInboundFromString(string direction)
    {
        if (string.IsNullOrEmpty(direction)) return "outbound";
        var d = direction.ToLowerInvariant();
        if (d == "inbound" || d.Contains("inbound") || d.Contains("terminating"))
            return "inbound";
        return "outbound";
    }

    private static int ParseDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return 0;
        var clean = duration.Split('.')[0];
        return int.TryParse(clean, out var s) ? s : 0;
    }
}
