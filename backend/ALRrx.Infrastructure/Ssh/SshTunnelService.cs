using ALRrx.Application.Interfaces;
using ALRrx.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace ALRrx.Infrastructure.Ssh;

public sealed class SshTunnelService : ISshTunnelService
{
    private readonly ConnectionConfig _config;
    private readonly ILogger<SshTunnelService> _logger;
    private SshClient? _client;
    private ForwardedPortLocal? _port;

    public SshTunnelService(ConnectionConfig config, ILogger<SshTunnelService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool IsConnected => _client?.IsConnected == true && _port?.IsStarted == true;
    public string LocalHost => _config.LocalPort.ToString();
    public int LocalPort => _config.LocalPort;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        await Task.Run(() =>
        {
            var connectionInfo = BuildConnectionInfo();

            _client = new SshClient(connectionInfo);
            _client.Connect();

            _port = new ForwardedPortLocal(
                "127.0.0.1",
                (uint)_config.LocalPort,
                _config.RemoteHost,
                (uint)_config.RemotePort);

            _client.AddForwardedPort(_port);
            _port.Start();

            _logger.LogInformation(
                "SSH tunnel established: localhost:{LocalPort} -> {RemoteHost}:{RemotePort}",
                _config.LocalPort, _config.RemoteHost, _config.RemotePort);
        }, ct);
    }

    public Task DisconnectAsync()
    {
        _port?.Stop();
        _client?.Disconnect();
        _logger.LogInformation("SSH tunnel disconnected");
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _client?.Dispose();
        _port?.Dispose();
    }

    private ConnectionInfo BuildConnectionInfo()
    {
        if (!string.IsNullOrEmpty(_config.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrEmpty(_config.PrivateKeyPassphrase)
                ? new PrivateKeyFile(_config.PrivateKeyPath)
                : new PrivateKeyFile(_config.PrivateKeyPath, _config.PrivateKeyPassphrase);

            return new ConnectionInfo(
                _config.Host,
                _config.Port,
                _config.Username,
                new PrivateKeyAuthenticationMethod(_config.Username, keyFile));
        }

        return new ConnectionInfo(
            _config.Host,
            _config.Port,
            _config.Username,
            new PasswordAuthenticationMethod(_config.Username, _config.Password));
    }
}
