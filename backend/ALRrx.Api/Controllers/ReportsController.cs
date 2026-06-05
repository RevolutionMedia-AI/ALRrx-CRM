using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly GetAvailableQueriesUseCase _getQueries;
    private readonly GetReportUseCase _getReport;
    private readonly GetAgentPerformanceWithSalesUseCase _agentPerformanceWithSales;
    private readonly TimeFilterValidator _validator;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        GetAvailableQueriesUseCase getQueries,
        GetReportUseCase getReport,
        GetAgentPerformanceWithSalesUseCase agentPerformanceWithSales,
        TimeFilterValidator validator,
        ILogger<ReportsController> logger)
    {
        _getQueries = getQueries;
        _getReport = getReport;
        _agentPerformanceWithSales = agentPerformanceWithSales;
        _validator = validator;
        _logger = logger;
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
        _logger.LogInformation("GetReport request | reportId: {ReportId} | period: {Period} | customStart: {CustomStart} | customEnd: {CustomEnd}", reportId, period, customStart, customEnd);

        var filter = new TimeFilterDto
        {
            Period = period,
            CustomStart = customStart,
            CustomEnd = customEnd
        };

        var validationResult = await _validator.ValidateAsync(filter, ct);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
            return BadRequest(validationResult.Errors);
        }

        var result = await _getReport.ExecuteAsync(reportId, filter, ct);
        return Ok(result);
    }

    [HttpGet("agent_performance_with_sales")]
    public async Task<ActionResult<ReportDto>> GetAgentPerformanceWithSales(
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
        {
            return BadRequest(validationResult.Errors);
        }

        var result = await _agentPerformanceWithSales.ExecuteAsync(filter, ct);
        return Ok(result);
    }
}
