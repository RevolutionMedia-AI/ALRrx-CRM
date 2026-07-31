using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ALRrx.Api.Realtime;

public sealed class SignalRSalesBroadcastService : ISalesBroadcastService
{
    private readonly IHubContext<Hubs.DashboardHub, IDashboardHubService> _hub;
    private readonly ILogger<SignalRSalesBroadcastService> _logger;

    public SignalRSalesBroadcastService(
        IHubContext<Hubs.DashboardHub, IDashboardHubService> hub,
        ILogger<SignalRSalesBroadcastService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task NotifyTvSaleAsync(string salesRep, string bundle, decimal amount, int todaysCount, CancellationToken ct = default)
    {
        try
        {
            await _hub.Clients.All.BroadcastTvSaleAsync(salesRep, bundle, amount, todaysCount, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR broadcast failed for {SalesRep}", salesRep);
        }
    }
}