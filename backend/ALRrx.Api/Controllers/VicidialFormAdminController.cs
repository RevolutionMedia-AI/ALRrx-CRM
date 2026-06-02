using System.ComponentModel.DataAnnotations;
using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/vicidial-form/admin")]
[Authorize(Roles = "Admin,Supervisor")]
public sealed class VicidialFormAdminController : ControllerBase
{
    private readonly GetAllVicidialSalesUseCase _list;

    public VicidialFormAdminController(GetAllVicidialSalesUseCase list)
    {
        _list = list;
    }

    [HttpGet("sales")]
    public async Task<ActionResult<List<VicidialSaleDto>>> ListAll(
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var rows = await _list.ExecuteAsync(from, to, limit, ct);
        return Ok(rows);
    }
}
