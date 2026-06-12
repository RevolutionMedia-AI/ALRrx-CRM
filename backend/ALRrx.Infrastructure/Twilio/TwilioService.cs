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

        var callsTask = FetchAllCallsAsync(start, end, ct);
        var usageTask = FetchUsageCostsAsync(start, end, ct);
        await Task.WhenAll(callsTask, usageTask);

        var calls = await callsTask;
        var (totalCost, inboundCost, outboundCost, currency) = await usageTask;
        _logger.LogInformation("[Twilio] GetSummaryAsync returned {Count} calls, totalCost={Cost} (in={In} out={Out}) {Currency} for period '{Period}'",
            calls.Count, totalCost, inboundCost, outboundCost, currency, period);

        var inbound = calls.Where(c => c.Direction == "inbound").ToList();
        var outbound = calls.Where(c => c.Direction != "inbound").ToList();

        var summary = new TwilioSummaryDto
        {
            TotalCost = totalCost,
            TotalCalls = calls.Count,
            TotalMinutes = (int)calls.Sum(c => c.DurationSeconds / 60.0),
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

        var callsTask = FetchAllCallsAsync(start, end, ct);
        var dailyCostTask = FetchDailyUsageCostsAsync(start, end, ct);
        await Task.WhenAll(callsTask, dailyCostTask);

        var calls = await callsTask;
        var dailyCost = await dailyCostTask;

        var callsByDate = calls
            .GroupBy(c => c.StartTime.Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var allDates = callsByDate.Keys
            .Union(dailyCost.Keys)
            .OrderBy(d => d)
            .ToList();

        var result = allDates
            .Select(d => new TwilioDailyCostDto
            {
                Date = d,
                Cost = dailyCost.TryGetValue(d, out var c) ? c : 0m,
                CallCount = callsByDate.TryGetValue(d, out var list) ? list.Count : 0,
                Minutes = callsByDate.TryGetValue(d, out var list2) ? (int)list2.Sum(c => c.DurationSeconds / 60.0) : 0
            })
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

    /// <summary>
    /// Fetch real billed cost from Twilio Usage/Records.json.
    /// This is the source of truth for SIP trunking costs — Calls.json
    /// returns price=null for trunked calls, so we MUST aggregate from
    /// usage records (categories sip-trunking-inbound-price,
    /// sip-trunking-outbound-*-price, sip-recording-storage, etc).
    /// </summary>
    private async Task<(decimal total, decimal inbound, decimal outbound, string currency)> FetchUsageCostsAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        decimal total = 0m, inbound = 0m, outbound = 0m;
        string currency = "USD";
        var allRecords = await FetchAllUsageRecordsAsync(start, end, ct);
        _logger.LogInformation("[Twilio] FetchUsageCostsAsync: {Count} usage records between {Start:o} and {End:o}",
            allRecords.Count, start, end);

        foreach (var rec in allRecords)
        {
            if (!rec.Category.StartsWith("sip-", StringComparison.OrdinalIgnoreCase))
                continue;

            if (rec.Price <= 0m) continue;

            total += rec.Price;
            if (!string.IsNullOrEmpty(rec.Currency))
                currency = rec.Currency;

            var cat = rec.Category.ToLowerInvariant();
            if (cat.Contains("inbound") || cat.Contains("origination"))
                inbound += rec.Price;
            else if (cat.Contains("outbound") || cat.Contains("termination"))
                outbound += rec.Price;
        }

        return (total, inbound, outbound, currency);
    }

    private async Task<Dictionary<DateTime, decimal>> FetchDailyUsageCostsAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        var byDate = new Dictionary<DateTime, decimal>();
        var allRecords = await FetchAllUsageRecordsAsync(start, end, ct);

        foreach (var rec in allRecords)
        {
            if (!rec.Category.StartsWith("sip-", StringComparison.OrdinalIgnoreCase))
                continue;
            if (rec.Price <= 0m) continue;

            var date = TimeZoneHelper.ToPst(rec.StartDate).Date;
            if (!byDate.TryAdd(date, rec.Price))
                byDate[date] += rec.Price;
        }

        return byDate;
    }

    private async Task<List<TwilioUsageRecord>> FetchAllUsageRecordsAsync(
        DateTime start, DateTime end, CancellationToken ct)
    {
        var all = new List<TwilioUsageRecord>();
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
                sb.Append($"/2010-04-01/Accounts/{_accountSid}/Usage/Records.json?PageSize={pageSize}");
                sb.Append($"&StartTime>={Uri.EscapeDataString(start.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                sb.Append($"&EndTime<={Uri.EscapeDataString(end.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"))}");
                url = AbsoluteUrl(sb.ToString());
            }

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);

            _logger.LogInformation("[Twilio] HTTP GET Usage/Records page {Page}: {Url}", page, url);
            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[Twilio] Usage/Records HTTP {Status}: {Body}", response.StatusCode,
                    body.Length > 300 ? body.Substring(0, 300) + "..." : body);
                break;
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("usage_records", out var records))
                break;

            nextPageUri = doc.RootElement.TryGetProperty("next_page_uri", out var npt) ? npt.GetString() : null;

            foreach (var rec in records.EnumerateArray())
            {
                var parsed = ParseUsageRecord(rec);
                if (parsed != null) all.Add(parsed);
            }

            page++;
            if (page > 200) break;

        }
        while (!string.IsNullOrEmpty(nextPageUri));

        return all;
    }

    private static TwilioUsageRecord? ParseUsageRecord(JsonElement el)
    {
        try
        {
            var category = el.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
            var description = el.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
            var priceStr = el.TryGetProperty("price", out var p) ? p.GetString() : null;
            var priceUnit = el.TryGetProperty("price_unit", out var pu) ? pu.GetString() ?? "USD" : "USD";
            var startDateStr = el.TryGetProperty("start_date", out var sd) ? sd.GetString() : null;
            var endDateStr = el.TryGetProperty("end_date", out var ed) ? ed.GetString() : null;
            var usageStr = el.TryGetProperty("usage", out var u) ? u.GetString() : null;
            var usageUnit = el.TryGetProperty("usage_unit", out var uu) ? uu.GetString() ?? "" : "";
            var count = el.TryGetProperty("count", out var cnt) && cnt.TryGetInt32(out var cv) ? cv : 0;

            var price = 0m;
            if (!string.IsNullOrEmpty(priceStr))
                decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out price);
            price = Math.Abs(price);

            DateTime startDate = DateTime.MinValue;
            if (!string.IsNullOrEmpty(startDateStr) && DateTime.TryParse(startDateStr, out var sdp))
                startDate = DateTime.SpecifyKind(sdp, DateTimeKind.Utc);

            return new TwilioUsageRecord
            {
                Category = category,
                Description = description,
                Price = price,
                Currency = priceUnit,
                StartDate = startDate,
                EndDate = !string.IsNullOrEmpty(endDateStr) && DateTime.TryParse(endDateStr, out var edp)
                    ? DateTime.SpecifyKind(edp, DateTimeKind.Utc) : startDate,
                Usage = usageStr ?? "",
                UsageUnit = usageUnit,
                Count = count
            };
        }
        catch
        {
            return null;
        }
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

internal sealed class TwilioUsageRecord
{
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Usage { get; set; } = "";
    public string UsageUnit { get; set; } = "";
    public int Count { get; set; }
}
