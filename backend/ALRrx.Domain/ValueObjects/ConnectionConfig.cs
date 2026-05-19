namespace ALRrx.Domain.ValueObjects;

public sealed record ConnectionConfig
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 22;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string PrivateKeyPath { get; init; } = string.Empty;
    public string PrivateKeyPassphrase { get; init; } = string.Empty;
    public int LocalPort { get; init; } = 3307;
    public string RemoteHost { get; init; } = "127.0.0.1";
    public int RemotePort { get; init; } = 3306;
    public string Database { get; init; } = string.Empty;
    public string DatabaseUser { get; init; } = string.Empty;
    public string DatabasePassword { get; init; } = string.Empty;
}
