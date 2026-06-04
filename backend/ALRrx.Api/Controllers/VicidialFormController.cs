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
    private readonly GetActiveAltrxAgentsUseCase _activeAgents;
    private readonly ILogger<VicidialFormController> _logger;

    public VicidialFormController(
        SubmitVicidialSaleUseCase submit,
        GetVicidialSalesUseCase list,
        GetActiveAltrxAgentsUseCase activeAgents,
        ILogger<VicidialFormController> logger)
    {
        _submit = submit;
        _list = list;
        _activeAgents = activeAgents;
        _logger = logger;
    }

    [HttpPost("sale")]
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
    public async Task<ActionResult<List<VicidialSaleDto>>> ListSales(
        [FromQuery] string? salesRep = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
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

    [HttpGet("active-agents")]
    public async Task<ActionResult<List<ActiveAltrxAgentDto>>> ListActiveAgents(CancellationToken ct = default)
    {
        try
        {
            var agents = await _activeAgents.ExecuteAsync(ct);
            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load active ALTRX agents");
            return StatusCode(500, new { error = "Could not load active agents" });
        }
    }
}
