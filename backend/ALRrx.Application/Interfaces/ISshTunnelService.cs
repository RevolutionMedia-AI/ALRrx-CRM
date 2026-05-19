namespace ALRrx.Application.Interfaces;

public interface ISshTunnelService : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct = default);
    bool IsConnected { get; }
    string LocalHost { get; }
    int LocalPort { get; }
    Task DisconnectAsync();
}
