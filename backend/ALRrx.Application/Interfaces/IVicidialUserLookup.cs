namespace ALRrx.Application.Interfaces;

public interface IVicidialUserLookup
{
    Task<VicidialUserInfo?> GetActiveAltrxUserAsync(string user, CancellationToken ct = default);
}

public sealed record VicidialUserInfo(string User, string FullName);
