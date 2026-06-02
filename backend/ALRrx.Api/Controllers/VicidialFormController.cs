using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/vicidial-form")]
public sealed class VicidialFormController : ControllerBase
{
    private readonly AuthenticateVicidialFormUseCase _auth;
    private readonly SubmitVicidialSaleUseCase _submit;
    private readonly GetVicidialSalesUseCase _list;
    private readonly ILogger<VicidialFormController> _logger;

    public VicidialFormController(
        AuthenticateVicidialFormUseCase auth,
        SubmitVicidialSaleUseCase submit,
        GetVicidialSalesUseCase list,
        ILogger<VicidialFormController> logger)
    {
        _auth = auth;
        _submit = submit;
        _list = list;
        _logger = logger;
    }

    [HttpPost("auth")]
    [AllowAnonymous]
    public async Task<ActionResult<VicidialAuthResponse>> Authenticate(
        [FromBody] VicidialAuthRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
            return BadRequest(new { error = "Key is required" });

        var result = await _auth.ExecuteAsync(request, ct);
        if (result is null)
            return Unauthorized(new { error = "Invalid form key" });

        return Ok(result);
    }

    [HttpPost("sale")]
    [Authorize(Roles = "VicidialForm")]
    public async Task<ActionResult> SubmitSale(
        [FromBody] VicidialSaleRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var newId = await _submit.ExecuteAsync(request, ct);
            return Ok(new { id = newId, message = "Sale recorded successfully" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("sales")]
    [Authorize(Roles = "VicidialForm")]
    public async Task<ActionResult<List<VicidialSaleDto>>> ListSales(
        [FromQuery, Required] string salesRep,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(salesRep))
            return BadRequest(new { error = "salesRep query parameter is required" });

        try
        {
            var rows = await _list.ExecuteAsync(salesRep, from, to, limit, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
