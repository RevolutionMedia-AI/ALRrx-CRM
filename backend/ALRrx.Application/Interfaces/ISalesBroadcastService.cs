namespace ALRrx.Application.Interfaces;

public interface ISalesBroadcastService
{
    Task NotifyTvSaleAsync(string salesRep, string bundle, decimal amount, int todaysCount, CancellationToken ct = default);
}