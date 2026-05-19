using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace ALRrx.Api.Hubs;

public sealed class DashboardHub : Hub<IDashboardHubService>
{
    public async Task RequestDashboardUpdate(string period)
    {
        await Clients.Caller.BroadcastDashboardUpdateAsync(
            new DashboardSummaryDto { LastUpdated = DateTime.UtcNow });
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.NotifyErrorAsync("Connected to dashboard hub");
    }
}
