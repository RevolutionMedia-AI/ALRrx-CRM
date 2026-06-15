using ALRrx.Application.DTOs;
using ALRrx.Application.Interfaces;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/vicidial-form")]
[AllowAnonymous]
[EnableRateLimiting("vicidial")]
public sealed class VicidialFormController : ControllerBase
{
    private readonly SubmitVicidialSaleUseCase _submit;
    private readonly GetVicidialSalesUseCase _list;
    private readonly UpdateVicidialSaleUseCase _update;
    private readonly DeleteVicidialSaleUseCase _delete;
    private readonly GetActiveAltrxAgentsUseCase _activeAgents;
    private readonly IActiveAgentsRepository _agentRepo;
    private readonly GetVicidialLeadByIdUseCase _leadLookup;
    private readonly GetEnrichedSalesUseCase _enrichedSales;
    private readonly ILogger<VicidialFormController> _logger;

    public VicidialFormController(
        SubmitVicidialSaleUseCase submit,
        GetVicidialSalesUseCase list,
        UpdateVicidialSaleUseCase update,
        DeleteVicidialSaleUseCase delete,
        GetActiveAltrxAgentsUseCase activeAgents,
        IActiveAgentsRepository agentRepo,
        GetVicidialLeadByIdUseCase leadLookup,
        GetEnrichedSalesUseCase enrichedSales,
        ILogger<VicidialFormController> logger)
    {
        _submit = submit;
        _list = list;
        _update = update;
        _delete = delete;
        _activeAgents = activeAgents;
        _agentRepo = agentRepo;
        _leadLookup = leadLookup;
        _enrichedSales = enrichedSales;
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

    [HttpGet("agent/{user}")]
    public async Task<ActionResult<ActiveAltrxAgentDto>> GetAgentByUser(string user, CancellationToken ct = default)
    {
        try
        {
            var agent = await _agentRepo.GetByUserAsync(user, ct);
            if (agent == null) return NotFound(new { error = $"Agent '{user}' not found in VICIdial" });
            return Ok(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load agent {User} from VICIdial", user);
            return StatusCode(503, new { error = "Cannot reach VICIdial database" });
        }
    }

    [HttpGet("lead/{leadId:int}")]
    public async Task<ActionResult<VicidialLeadDto>> GetLeadById(int leadId, CancellationToken ct = default)
    {
        var result = await _leadLookup.ExecuteAsync(leadId, ct);
        return result.Status switch
        {
            LeadLookupStatus.Found => Ok(result.Lead),
            LeadLookupStatus.NotFound => NotFound(new { error = result.Message }),
            LeadLookupStatus.InvalidInput => BadRequest(new { error = result.Message }),
            LeadLookupStatus.ConnectionError => StatusCode(503, new { error = result.Message }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    [HttpGet("sales/enriched")]
    public async Task<ActionResult<List<VicidialSaleEnrichedDto>>> ListEnrichedSales(
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _enrichedSales.ExecuteAsync(from, to, limit, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load enriched Vicidial sales");
            return StatusCode(500, new { error = "Could not load enriched sales" });
        }
    }

    [HttpGet("sales/by-lead/{leadId:int}")]
    public async Task<ActionResult<List<VicidialSaleEnrichedDto>>> GetSalesByLeadId(
        int leadId,
        CancellationToken ct = default)
    {
        try
        {
            var rows = await _enrichedSales.GetByLeadIdAsync(leadId, ct);
            return Ok(rows);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load sales for lead #{LeadId}", leadId);
            return StatusCode(500, new { error = "Could not load sales for lead" });
        }
    }
}
