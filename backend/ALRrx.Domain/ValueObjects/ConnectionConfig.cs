namespace ALRrx.Domain.ValueObjects;

public sealed record ConnectionConfig
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string PrivateKeyPath { get; init; } = string.Empty;
    public string PrivateKeyPassphrase { get; init; } = string.Empty;
    public string RemoteHost { get; init; } = string.Empty;
    public int RemotePort { get; init; }
    public string DatabaseHost { get; init; } = string.Empty;
    public int DatabasePort { get; init; }
    public string Database { get; init; } = string.Empty;
    public string DatabaseUser { get; init; } = string.Empty;
    public string DatabasePassword { get; init; } = string.Empty;
}
