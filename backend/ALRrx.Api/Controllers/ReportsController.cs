using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly GetAvailableQueriesUseCase _getQueries;
    private readonly GetReportUseCase _getReport;
    private readonly TimeFilterValidator _validator;

    public ReportsController(
        GetAvailableQueriesUseCase getQueries,
        GetReportUseCase getReport,
        TimeFilterValidator validator)
    {
        _getQueries = getQueries;
        _getReport = getReport;
        _validator = validator;
    }

    [HttpGet]
    public ActionResult<IReadOnlyCollection<QueryDefinitionDto>> GetAvailableQueries()
    {
        return Ok(_getQueries.Execute());
    }

    [HttpGet("{reportId}")]
    public async Task<ActionResult<ReportDto>> GetReport(
        string reportId,
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        var filter = new TimeFilterDto
        {
            Period = period,
            CustomStart = customStart,
            CustomEnd = customEnd
        };

        var validationResult = await _validator.ValidateAsync(filter, ct);
        if (!validationResult.IsValid)
            return BadRequest(validationResult.Errors);

        var result = await _getReport.ExecuteAsync(reportId, filter, ct);
        return Ok(result);
    }
}
