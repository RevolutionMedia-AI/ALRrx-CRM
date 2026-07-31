using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/tv")]
[Authorize]
public sealed class TvController : ControllerBase
{
    private readonly IVicidialSalesRepository _sales;

    public TvController(IVicidialSalesRepository sales)
    {
        _sales = sales;
    }

    [HttpGet("sales-by-agent")]
    public async Task<ActionResult<List<TvAgentSalesRow>>> SalesByAgentToday(CancellationToken ct)
    {
        var range = BuildTodayPst();
        var rows = await _sales.GetFormSalesByAgentAsync(range.from, range.to, ct);

        var dto = rows.Values
            .OrderByDescending(r => r.Count)
            .ThenBy(r => r.SalesRep, StringComparer.OrdinalIgnoreCase)
            .Select(r => new TvAgentSalesRow(r.SalesRep, r.Count, r.Amount))
            .ToList();

        return Ok(dto);
    }

    private static (string from, string to) BuildTodayPst()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Tijuana");
        var local = TimeZoneInfo.ConvertTime(DateTime.UtcNow, tz);
        var start = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0);
        var end = start.AddDays(1);
        return (start.ToString("yyyy-MM-dd HH:mm:ss"), end.ToString("yyyy-MM-dd HH:mm:ss"));
    }
}

public sealed record TvAgentSalesRow(string SalesRep, int FormSalesCount, decimal FormSalesAmount);