using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class SubmitVicidialSaleUseCase
{
    private readonly IVicidialSalesRepository _repo;
    private readonly IActiveAgentsRepository _agents;
    private readonly ILogger<SubmitVicidialSaleUseCase> _logger;
    private readonly ISalesBroadcastService? _broadcast;

    public SubmitVicidialSaleUseCase(
        IVicidialSalesRepository repo,
        IActiveAgentsRepository agents,
        ILogger<SubmitVicidialSaleUseCase> logger,
        ISalesBroadcastService? broadcast = null)
    {
        _repo = repo;
        _agents = agents;
        _logger = logger;
        _broadcast = broadcast;
    }

    public async Task<int> ExecuteAsync(VicidialSaleRequest request, string? idempotencyKey = null, CancellationToken ct = default)
    {
        if (request.LeadId is not null and <= 0)
            throw new ArgumentException("LeadId must be greater than zero if provided");
        if (string.IsNullOrWhiteSpace(request.SalesRep))
            throw new ArgumentException("SalesRep is required");
        if (string.IsNullOrWhiteSpace(request.ClientName))
            throw new ArgumentException("ClientName is required");
        if (string.IsNullOrWhiteSpace(request.ClientEmail))
            throw new ArgumentException("ClientEmail is required");
        if (string.IsNullOrWhiteSpace(request.ClientPhone))
            throw new ArgumentException("ClientPhone is required");
        if (request.Amount <= 0)
            throw new ArgumentException("Amount must be greater than zero");
        if (string.IsNullOrWhiteSpace(request.ConfirmationUrl))
            throw new ArgumentException("ConfirmationUrl is required");
        if (!Uri.TryCreate(request.ConfirmationUrl.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("ConfirmationUrl must be a valid http(s) URL");
        if (!BundleTypeExtensions.TryParseBundle(request.Bundle, out var bundleType))
            throw new ArgumentException($"Invalid bundle: '{request.Bundle}'. Allowed: GLP-1 1/3/6/12 Months, GLP-1/GIP 1/3/6/12 Months");

        var bundleDisplayName = bundleType.ToDisplayName();

        // The caller is an automated agent with no session, so the SalesRep it
        // claims is the only identity signal on the request. Require it to be an
        // agent Vicidial still has active before anything is written.
        var rep = request.SalesRep.Trim();
        if (!ActiveAgentMatcher.IsKnownAgent(await _agents.GetActiveAltrxAgentsAsync(ct), rep))
        {
            _logger.LogWarning("Rejected sale: SalesRep '{SalesRep}' is not an active ALTRX agent", rep);
            throw new UnauthorizedAccessException($"SalesRep '{rep}' is not an active ALTRX agent");
        }

        // MySQL stores SaleDate in a naive DATETIME column and every report
        // query filters it against America/Tijuana wall-clock strings. A caller
        // that sends an offset-aware timestamp (what a phone AI agent naturally
        // produces via toISOString()) would otherwise persist the UTC clock and
        // land the sale on the wrong day. Normalize once, here, so every caller
        // — human form or agent — stores the same thing.
        request.SaleDate = ToBusinessWallClock(request.SaleDate);

        // An agent that times out mid-request will retry. Without a key each
        // retry is a second sale. Replay the original id instead of inserting.
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await _repo.GetByIdempotencyKeyAsync(idempotencyKey.Trim(), ct);
            if (existing is not null)
            {
                _logger.LogInformation("Vicidial sale idempotent replay: key={Key} -> existing #{Id}", idempotencyKey, existing);
                return existing.Value;
            }
        }

        var newId = await _repo.InsertAsync(request, bundleDisplayName, idempotencyKey, ct);
        var source = request.LeadId.HasValue ? "VicidialForm" : "ManualForm";
        _logger.LogInformation("Vicidial sale #{Id} submitted: leadId={LeadId}, rep={Rep}, bundle={Bundle}, ${Amount}, source={Source}",
            newId, request.LeadId, request.SalesRep, bundleDisplayName, request.Amount, source);

        // Notify TV clients so the leaderboard updates live. Best-effort —
        // a SignalR outage must not block the sale submission.
        if (_broadcast is not null)
        {
            try
            {
                var range = VicidialDayRange.BuildToday();
                var todaysCount = await GetTodaysCountForRepAsync(request.SalesRep, range.From, range.To, ct);
                await _broadcast.NotifyTvSaleAsync(request.SalesRep, bundleDisplayName, request.Amount, todaysCount, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TV broadcast failed for sale #{Id}", newId);
            }
        }

        return newId;
    }

    private async Task<int> GetTodaysCountForRepAsync(string salesRep, string from, string to, CancellationToken ct)
    {
        var rows = await _repo.GetFormSalesByAgentAsync(from, to, ct);
        return rows.TryGetValue(salesRep, out var row) ? row.Count : 0;
    }

    /// <summary>
    /// Projects <paramref name="saleDate"/> onto the business-timezone wall
    /// clock, discarding the offset. An Unspecified value is already a bare
    /// wall clock (that is what the human form sends) and passes through.
    /// </summary>
    private static DateTime ToBusinessWallClock(DateTime saleDate) => saleDate.Kind switch
    {
        DateTimeKind.Utc => TimeZoneInfo.ConvertTimeFromUtc(saleDate, BusinessTimeZone.Current),
        DateTimeKind.Local => TimeZoneInfo.ConvertTime(saleDate, BusinessTimeZone.Current),
        _ => DateTime.SpecifyKind(saleDate, DateTimeKind.Unspecified),
    };
}

internal static class BusinessTimeZone
{
    private static readonly Lazy<TimeZoneInfo> Cached = new(
        () => TimeZoneInfo.FindSystemTimeZoneById("America/Tijuana"));

    public static TimeZoneInfo Current => Cached.Value;
}

internal static class VicidialDayRange
{
    public static (string From, string To) BuildToday()
    {
        var local = TimeZoneInfo.ConvertTime(DateTime.UtcNow, BusinessTimeZone.Current);
        var start = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0);
        var end = start.AddDays(1);
        return (start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
