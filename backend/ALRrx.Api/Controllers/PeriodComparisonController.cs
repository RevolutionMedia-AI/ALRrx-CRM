using ALRrx.Application.DTOs;
using ALRrx.Application.UseCases;
using ALRrx.Infrastructure.Export;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InfraKpiRow = ALRrx.Infrastructure.Export.KpiRow;
using InfraDispositionRow = ALRrx.Infrastructure.Export.DispositionRow;
using InfraAgentComparisonRow = ALRrx.Infrastructure.Export.AgentComparisonRow;
using InfraContactComparison = ALRrx.Infrastructure.Export.ContactComparison;

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
                Period1Kpis = result.Period1Kpis.Select(k => new InfraKpiRow { Label = k.Label, Value = k.Value, Color = k.Color }).ToList(),
                Period2Kpis = result.Period2Kpis.Select(k => new InfraKpiRow { Label = k.Label, Value = k.Value, Color = k.Color }).ToList(),
                KpiChanges = result.KpiChanges.Select(k => new InfraKpiRow { Label = k.Label, Value = k.Value, Color = k.Color }).ToList(),
                Agents = result.Agents.Select(a => new InfraAgentComparisonRow
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
                Period1Dispositions = result.Period1Dispositions.Select(d => new InfraDispositionRow { Status = d.Status, Total = d.Total, Percentage = d.Percentage }).ToList(),
                Period2Dispositions = result.Period2Dispositions.Select(d => new InfraDispositionRow { Status = d.Status, Total = d.Total, Percentage = d.Percentage }).ToList(),
                ContactComparison = result.ContactComparison != null ? new InfraContactComparison
                {
                    Period1Contacts = result.ContactComparison.Period1Contacts,
                    Period2Contacts = result.ContactComparison.Period2Contacts,
                    Period1NoContacts = result.ContactComparison.Period1NoContacts,
                    Period2NoContacts = result.ContactComparison.Period2NoContacts,
                    Period1Rate = result.ContactComparison.Period1Rate,
                    Period2Rate = result.ContactComparison.Period2Rate
                } : null
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