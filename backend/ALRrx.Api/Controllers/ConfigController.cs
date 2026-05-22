using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class ConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public ConfigController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("google-client-id")]
    public ActionResult GetGoogleClientId()
    {
        var clientId = _config["Google:ClientId"] ?? string.Empty;
        return Ok(new { clientId });
    }
}
