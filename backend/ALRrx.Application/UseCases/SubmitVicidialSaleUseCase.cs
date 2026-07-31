using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ALRrx.Application.UseCases;

public sealed class SubmitVicidialSaleUseCase
{
    private readonly IVicidialSalesRepository _repo;
    private readonly ILogger<SubmitVicidialSaleUseCase> _logger;
    private readonly ISalesBroadcastService? _broadcast;

    public SubmitVicidialSaleUseCase(
        IVicidialSalesRepository repo,
        ILogger<SubmitVicidialSaleUseCase> logger,
        ISalesBroadcastService? broadcast = null)
    {
        _repo = repo;
        _logger = logger;
        _broadcast = broadcast;
    }

    public async Task<int> ExecuteAsync(VicidialSaleRequest request, CancellationToken ct = default)
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

        var newId = await _repo.InsertAsync(request, bundleDisplayName, ct);
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
}

internal static class VicidialDayRange
{
    public static (string From, string To) BuildToday()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Tijuana");
        var local = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz);
        var start = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0);
        var end = start.AddDays(1);
        return (start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}
