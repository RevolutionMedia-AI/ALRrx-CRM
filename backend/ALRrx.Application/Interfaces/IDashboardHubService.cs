using ALRrx.Application.DTOs;

namespace ALRrx.Application.Interfaces;

public interface IDashboardHubService
{
    Task BroadcastDashboardUpdateAsync(DashboardSummaryDto data, CancellationToken ct = default);
    Task NotifyErrorAsync(string message, CancellationToken ct = default);
}
