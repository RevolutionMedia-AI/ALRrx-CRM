using ALRrx.Application.DTOs;
using ALRrx.Application.Helpers;
using ALRrx.Application.Interfaces;
using ALRrx.Application.UseCases;
using ALRrx.Application.Validators;
using ALRrx.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ALRrx.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly GetDashboardDataUseCase _getDashboardData;
    private readonly TimeFilterValidator _validator;
    private readonly IGoogleSheetsImportService _googleSheetsService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        GetDashboardDataUseCase getDashboardData,
        TimeFilterValidator validator,
        IGoogleSheetsImportService googleSheetsService,
        ILogger<DashboardController> logger)
    {
        _getDashboardData = getDashboardData;
        _validator = validator;
        _googleSheetsService = googleSheetsService;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(">>> DASHBOARD REQUEST >>> period: {Period} | customStart: {CustomStart:dd/MM/yyyy HH:mm:ss} | customEnd: {CustomEnd:dd/MM/yyyy HH:mm:ss}", period, customStart, customEnd);

        var filter = new TimeFilterDto
        {
            Period = period,
            CustomStart = customStart,
            CustomEnd = customEnd
        };

        _logger.LogInformation(">>> FILTER CREATED >>> Period: {Period} | CustomStart: {CustomStart:dd/MM/yyyy HH:mm:ss} | CustomEnd: {CustomEnd:dd/MM/yyyy HH:mm:ss}", filter.Period, filter.CustomStart, filter.CustomEnd);

        var validationResult = await _validator.ValidateAsync(filter, ct);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Validation failed: {Errors}", string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));
            return BadRequest(validationResult.Errors);
        }

        var result = await _getDashboardData.ExecuteAsync(filter, ct);
        return Ok(result);
    }

    [HttpGet("google-sheets/sales")]
    public async Task<ActionResult<SalesSummaryDto>> GetGoogleSheetsSales(
        [FromQuery] string period = "Today",
        [FromQuery] DateTime? customStart = null,
        [FromQuery] DateTime? customEnd = null,
        [FromQuery] string? seller = null,
        [FromQuery] string? package = null,
        CancellationToken ct = default)
    {
        try
        {
            var filter = new TimeFilterDto
            {
                Period = period,
                CustomStart = customStart,
                CustomEnd = customEnd
            };
            var timeRange = TimeFilterHelper.BuildTimeRange(filter);

            var allSales = await _googleSheetsService.GetSalesAsync(ct);

            IEnumerable<SaleRecord> filtered = allSales.Where(s => s.SaleDate >= timeRange.Start && s.SaleDate <= timeRange.End);

            if (!string.IsNullOrWhiteSpace(seller) && seller != "all")
                filtered = filtered.Where(s => s.SellerName.Equals(seller, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(package) && package != "all")
                filtered = filtered.Where(s => s.Package.Equals(package, StringComparison.OrdinalIgnoreCase));

            var result = new SalesSummaryDto
            {
                TotalSales = filtered.Sum(s => s.Amount),
                TotalCount = filtered.Count(),
                LastSale = filtered.OrderByDescending(s => s.Timestamp).FirstOrDefault(),
                AllSales = filtered.OrderByDescending(s => s.Timestamp).ToList(),
                AvailableSellers = allSales.Select(s => s.SellerName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList(),
                AvailablePackages = allSales.Select(s => s.Package).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x).ToList()
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener ventas de Google Sheets");
            return Ok(new SalesSummaryDto());
        }
    }
}
