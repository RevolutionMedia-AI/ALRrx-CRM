using ALRrx.Application.DTOs;
using ALRrx.Infrastructure.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/data")]
[Authorize(Roles = "Admin,Supervisor")]
public sealed class DataController : ControllerBase
{
    private readonly MutationExecutor _mutation;

    public DataController(MutationExecutor mutation)
    {
        _mutation = mutation;
    }

    [HttpPut("{table}/edit/{id}")]
    public async Task<IActionResult> EditRow(
        string table, int id,
        [FromBody] EditRowRequest request,
        CancellationToken ct = default)
    {
        var safeTables = new[] { "vicidial_log", "vicidial_users", "vicidial_agent_log" };
        if (!safeTables.Contains(table))
            return BadRequest(new { error = "Table not editable" });

        var rows = await _mutation.UpdateRowAsync(table, "lead_id", id, request.Updates, ct);
        return Ok(new { affected = rows });
    }

    [HttpDelete("{table}/delete/{id}")]
    public async Task<IActionResult> DeleteRow(
        string table, int id,
        CancellationToken ct = default)
    {
        var safeTables = new[] { "vicidial_log", "vicidial_users", "vicidial_agent_log" };
        if (!safeTables.Contains(table))
            return BadRequest(new { error = "Table not deletable" });

        var rows = await _mutation.DeleteRowAsync(table, "lead_id", id, ct);
        return Ok(new { affected = rows });
    }
}
