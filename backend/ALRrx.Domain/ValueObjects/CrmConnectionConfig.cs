namespace ALRrx.Domain.ValueObjects;

public sealed record CrmConnectionConfig
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 3306;
    public string Database { get; init; } = string.Empty;
    public string User { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
