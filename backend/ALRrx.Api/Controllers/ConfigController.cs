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
        // Read from config — populated by either appsettings.json Google:ClientId
        // or env var Google__ClientId (double underscore = nested key in
        // ASP.NET Core configuration). On Northflank/Railway/Render etc,
        // set the env var as Google__ClientId=<your-client-id>.
        var clientId = _config["Google:ClientId"] ?? string.Empty;
        return Ok(new
        {
            clientId,
            // Diagnostics for the frontend: if configSource is null, the env
            // var is missing or empty. The frontend can show the exact
            // instruction ("set env var X") to the admin.
            configured = !string.IsNullOrEmpty(clientId),
            envVarName = "Google__ClientId",
        });
    }
}
