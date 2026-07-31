namespace ALRrx.Application.Interfaces;

public interface ISalesBroadcastService
{
    Task NotifyTvSaleAsync(string salesRep, int todaysCount, CancellationToken ct = default);
}