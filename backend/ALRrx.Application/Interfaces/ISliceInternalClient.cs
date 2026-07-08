namespace ALRrx.Application.Interfaces;

/// <summary>
/// Internal-only client used by the alrrx backend to mutate the Slice
/// email allow list when an admin grants/revokes platform access from
/// the admin panel. Runs in the same container as the slice API and
/// reaches it over the internal localhost address.
/// </summary>
public interface ISliceInternalClient
{
    Task AddAllowedEmailAsync(string email, CancellationToken ct = default);
    Task RemoveAllowedEmailAsync(string email, CancellationToken ct = default);
}
