using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Infrastructure.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/period-comparison")]
[Authorize]
public sealed class PeriodComparisonController : ControllerBase
{
    private readonly PeriodComparisonUseCase _useCase;
    private readonly IPeriodComparisonExcelService _excelService;
    private readonly ILogger<PeriodComparisonController> _logger;

    public PeriodComparisonController(
        PeriodComparisonUseCase useCase,
        IPeriodComparisonExcelService excelService,
        ILogger<PeriodComparisonController> logger)
    {
        _useCase = useCase;
        _excelService = excelService;
        _logger = logger;
    }

    [HttpPost("excel")]
    public async Task<IActionResult> ExportComparisonExcel([FromBody] PeriodComparisonRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var result = await _useCase.ExecuteAsync(request.Period1, request.Period2, ct);

            var excelData = new PeriodComparisonExcelData
            {
                Period1Label = result.Period1Label,
                Period2Label = result.Period2Label,
                Period1Kpis = result.Period1Kpis,
                Period2Kpis = result.Period2Kpis,
                KpiChanges = result.KpiChanges,
                Agents = result.Agents.Select(a => new AgentComparisonRow
                {
                    Name = a.Name,
                    User = a.User,
                    Period1Calls = a.Period1Calls,
                    Period2Calls = a.Period2Calls,
                    CallsChangePct = a.CallsChangePct,
                    Period1Sales = a.Period1Sales,
                    Period2Sales = a.Period2Sales,
                    SalesChangePct = a.SalesChangePct
                }).ToList(),
                Period1Dispositions = result.Period1Dispositions,
                Period2Dispositions = result.Period2Dispositions,
                ContactComparison = result.ContactComparison
            };

            var excelBytes = _excelService.GenerateComparisonExcel(excelData);
            var fileName = $"ALTRX_Period_Comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(excelBytes, _excelService.ContentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating period comparison Excel");
            return StatusCode(500, new { error = "Error generating period comparison", detail = ex.Message });
        }
    }
}