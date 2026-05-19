namespace ALRrx.Application.Interfaces;

public interface ISshTunnelService : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    Task DisconnectAsync();
}
