using Microsoft.AspNetCore.Mvc;
using Slice.Api.Auth;

namespace Slice.Api.Controllers;

/// <summary>
/// Internal-only endpoints used by the alrrx backend to mutate the Slice
/// email allow list at runtime when an admin grants/revokes platform
/// access from the admin panel.
///
/// Authenticated via a shared secret passed in the X-Internal-Token
/// header. The shared secret is configured via the Slice:InternalToken
/// environment variable (or appsettings) and must match the value
/// configured in the alrrx env (Alrrx:Slice:InternalToken).
///
/// In a production deployment this controller should be reachable only
/// from the alrrx side. In the combined Northflank container the alrrx
/// process reaches the slice via http://localhost:5001, so the endpoint
/// is not exposed externally; nginx routes /api/* to alrrx and
/// /api/slice/* to slice, neither of which forwards /api/internal/*.
/// </summary>
[ApiController]
[Route("api/internal")]
public sealed class InternalController : ControllerBase
{
    private readonly EmailAllowList _allowList;
    private readonly IConfiguration _config;
    private readonly ILogger<InternalController> _logger;

    public InternalController(EmailAllowList allowList, IConfiguration config, ILogger<InternalController> logger)
    {
        _allowList = allowList;
        _config = config;
        _logger = logger;
    }

    private bool IsAuthorized()
    {
        var expected = _config["Slice:InternalToken"];
        if (string.IsNullOrEmpty(expected)) return false;
        var supplied = Request.Headers["X-Internal-Token"].FirstOrDefault();
        return !string.IsNullOrEmpty(supplied) &&
               CryptographicEquals(expected, supplied);
    }

    private static bool CryptographicEquals(string a, string b)
    {
        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }

    public sealed record AllowEmailRequest(string Email);
    public sealed record AllowDomainRequest(string Domain);
    public sealed record AllowListResponse(int EmailCount, int DomainCount);

    [HttpPost("allowed-emails")]
    public ActionResult<AllowListResponse> AddEmail([FromBody] AllowEmailRequest body)
    {
        if (!IsAuthorized()) return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.Email))
            return BadRequest(new { error = "email is required" });
        _allowList.AddEmail(body.Email);
        _logger.LogInformation("Allow list: added email {Email} (now {Count})", body.Email, _allowList.EmailCount);
        return Ok(new AllowListResponse(_allowList.EmailCount, _allowList.DomainCount));
    }

    [HttpDelete("allowed-emails/{email}")]
    public ActionResult<AllowListResponse> RemoveEmail(string email)
    {
        if (!IsAuthorized()) return Unauthorized();
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "email is required" });
        _allowList.RemoveEmail(email);
        _logger.LogInformation("Allow list: removed email {Email} (now {Count})", email, _allowList.EmailCount);
        return Ok(new AllowListResponse(_allowList.EmailCount, _allowList.DomainCount));
    }

    [HttpPost("allowed-domains")]
    public ActionResult<AllowListResponse> AddDomain([FromBody] AllowDomainRequest body)
    {
        if (!IsAuthorized()) return Unauthorized();
        if (body is null || string.IsNullOrWhiteSpace(body.Domain))
            return BadRequest(new { error = "domain is required" });
        _allowList.AddDomain(body.Domain);
        _logger.LogInformation("Allow list: added domain {Domain}", body.Domain);
        return Ok(new AllowListResponse(_allowList.EmailCount, _allowList.DomainCount));
    }

    [HttpGet("allowed-emails")]
    public ActionResult<AllowListResponse> GetCounts()
    {
        if (!IsAuthorized()) return Unauthorized();
        return Ok(new AllowListResponse(_allowList.EmailCount, _allowList.DomainCount));
    }
}
