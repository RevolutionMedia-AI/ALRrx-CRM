using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/vicidial-form")]
[AllowAnonymous]
public sealed class VicidialFormController : ControllerBase
{
    private readonly SubmitVicidialSaleUseCase _submit;
    private readonly GetVicidialSalesUseCase _list;
    private readonly GenerateVicidialFormTokenUseCase _generateToken;
    private readonly ValidateVicidialFormTokenUseCase _validateToken;
    private readonly ILogger<VicidialFormController> _logger;

    public VicidialFormController(
        SubmitVicidialSaleUseCase submit,
        GetVicidialSalesUseCase list,
        GenerateVicidialFormTokenUseCase generateToken,
        ValidateVicidialFormTokenUseCase validateToken,
        ILogger<VicidialFormController> logger)
    {
        _submit = submit;
        _list = list;
        _generateToken = generateToken;
        _validateToken = validateToken;
        _logger = logger;
    }

    private VicidialFormIdentity? TryGetIdentity()
    {
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(auth) || !auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;
        var token = auth.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            return _validateToken.Execute(new VicidialFormAuthRequest { Token = token });
        }
        catch
        {
            return null;
        }
    }

    [HttpPost("auth")]
    public ActionResult<VicidialFormIdentity> Authenticate([FromBody] VicidialFormAuthRequest request)
    {
        try
        {
            var identity = _validateToken.Execute(request);
            return Ok(identity);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
    }

    [HttpGet("me")]
    public ActionResult<VicidialFormIdentity> Me()
    {
        var identity = TryGetIdentity();
        if (identity is null)
            return Unauthorized(new { error = "Missing or invalid bearer token" });
        return Ok(identity);
    }

    [HttpPost("sale")]
    public async Task<ActionResult> SubmitSale(
        [FromBody] VicidialSaleClientRequest request,
        CancellationToken ct = default)
    {
        var identity = TryGetIdentity();
        if (identity is null)
            return Unauthorized(new { error = "Missing or invalid bearer token" });

        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var newId = await _submit.ExecuteAsync(request, identity, ct);
            return Ok(new { id = newId, message = "Sale recorded successfully" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("sales")]
    public async Task<ActionResult<List<VicidialSaleDto>>> ListSales(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        var identity = TryGetIdentity();
        if (identity is null)
            return Unauthorized(new { error = "Missing or invalid bearer token" });

        try
        {
            var rows = await _list.ExecuteAsync(identity, from, to, limit, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("admin/sales")]
    [Authorize]
    public async Task<ActionResult<List<VicidialSaleDto>>> ListAllSalesAdmin(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _list.ExecuteAllAsync(from, to, limit, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
