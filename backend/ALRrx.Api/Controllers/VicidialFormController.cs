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
    private readonly UpdateVicidialSaleUseCase _update;
    private readonly DeleteVicidialSaleUseCase _delete;
    private readonly GetActiveAltrxAgentsUseCase _activeAgents;
    private readonly ILogger<VicidialFormController> _logger;

    public VicidialFormController(
        SubmitVicidialSaleUseCase submit,
        GetVicidialSalesUseCase list,
        UpdateVicidialSaleUseCase update,
        DeleteVicidialSaleUseCase delete,
        GetActiveAltrxAgentsUseCase activeAgents,
        ILogger<VicidialFormController> logger)
    {
        _submit = submit;
        _list = list;
        _update = update;
        _delete = delete;
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
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
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

    [HttpGet("sales/summary")]
    public async Task<ActionResult<SalesSummaryDto>> GetSalesSummary(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        try
        {
            var summary = await _list.ExecuteSummaryAsync(from, to, limit, ct);
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compute Vicidial sales summary");
            return StatusCode(500, new { error = "Could not compute sales summary" });
        }
    }

    [HttpPatch("sale/{id:int}")]
    public async Task<ActionResult> UpdateSale(
        int id,
        [FromBody] VicidialSaleUpdateRequest request,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var ok = await _update.ExecuteAsync(id, request, ct);
            if (!ok) return NotFound(new { error = $"Sale #{id} not found or no changes applied" });
            return Ok(new { id, message = "Sale updated successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Vicidial sale #{Id} update denied: {Reason}", id, ex.Message);
            return StatusCode(403, new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("sale/{id:int}")]
    public async Task<ActionResult> DeleteSale(
        int id,
        [FromQuery] string editorEmail = "",
        CancellationToken ct = default)
    {
        try
        {
            await _delete.ExecuteAsync(id, editorEmail, ct);
            return Ok(new { id, message = "Sale deleted successfully" });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Vicidial sale #{Id} delete denied: {Reason}", id, ex.Message);
            return StatusCode(403, new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
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
