using System.Collections.Concurrent;

namespace Slice.Api.Auth;

/// <summary>
/// Shared, mutable in-memory allow list of emails + domains authorized to
/// access Slice. Backed by a concurrent collection so the middleware
/// (per-request hot path) and the internal API (mutation) can share
/// state without locking.
///
/// Pre-populated from Slice:AllowedEmails / Slice:AllowedDomains config
/// at startup. Can be extended at runtime by the InternalController when
/// alrrx grants a user Slice access from the admin panel.
/// </summary>
public sealed class EmailAllowList
{
    private readonly ConcurrentDictionary<string, byte> _emails = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _domains = new(StringComparer.OrdinalIgnoreCase);

    public void LoadFromConfig(string[]? emails, string[]? domains)
    {
        if (emails != null) foreach (var e in emails) _emails[e] = 0;
        if (domains != null) foreach (var d in domains) _domains[d] = 0;
    }

    public bool IsAllowed(string email)
    {
        if (string.IsNullOrEmpty(email)) return false;
        if (_emails.ContainsKey(email)) return true;
        var at = email.IndexOf('@');
        if (at < 0) return false;
        var domain = email[(at + 1)..];
        return _domains.ContainsKey(domain);
    }

    public void AddEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        _emails[email.Trim()] = 0;
    }

    public void AddDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        _domains[domain.Trim()] = 0;
    }

    public void RemoveEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        _emails.TryRemove(email.Trim(), out _);
    }

    public int EmailCount => _emails.Count;
    public int DomainCount => _domains.Count;
}
