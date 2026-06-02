using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Domain.Entities;
using Slice.Domain.Interfaces;

namespace Slice.Api.Controllers;

/// <summary>
/// Gestiona el acceso, exportación y edición de los reportes generados por Slice.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class ReportsController : ControllerBase
{
    private readonly IReportRepository _reports;

    public ReportsController(IReportRepository reports) => _reports = reports;

    // ─── Read endpoints (all authenticated users) ─────────────────────────────

    /// <summary>
    /// Lista todos los reportes. Admins ven todos; otros usuarios solo ven los propios.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var email = GetCurrentEmail();
        var reports = User.IsInRole("Admin")
            ? await _reports.GetAllAsync()
            : await _reports.GetAllByEmailAsync(email);

        return Ok(reports.Select(ReportDtoMapper.ToSummary));
    }

    /// <summary>
    /// Retorna el reporte completo (con todas las filas). Admins ven cualquier reporte; otros solo el propio.
    /// </summary>
    [HttpGet("{reportId}")]
    public async Task<IActionResult> GetById(string reportId)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        if (!User.IsInRole("Admin") && !report.GeneratedByEmail.Equals(GetCurrentEmail(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        return Ok(report);
    }

    /// <summary>
    /// Retorna las series de datos del Daily Global para renderizar gráficas.
    /// Aplica la misma política de acceso que <see cref="GetById"/>.
    /// </summary>
    [HttpGet("{reportId}/charts/global")]
    public async Task<IActionResult> GetGlobalChart(string reportId)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        if (!User.IsInRole("Admin") && !report.GeneratedByEmail.Equals(GetCurrentEmail(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        return Ok(ReportDtoMapper.ToGlobalChart(report));
    }

    /// <summary>
    /// Descarga el reporte exportado en el formato indicado (<c>xlsx</c> o <c>csv</c>).
    /// El archivo se sirve en streaming sin cargarlo en memoria del servidor.
    /// </summary>
    [HttpGet("{reportId}/export/{format}")]
    public async Task<IActionResult> Export(string reportId, string format)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        if (!User.IsInRole("Admin") && !report.GeneratedByEmail.Equals(GetCurrentEmail(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        var (path, contentType, ext) = format.ToLowerInvariant() switch
        {
            "xlsx" => (report.MergedXlsxPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
            "csv"  => (report.MergedCsvPath,  "text/csv", "csv"),
            _      => throw new InvalidOperationException($"Unsupported format '{format}'. Use 'xlsx' or 'csv'.")
        };

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            return NotFound(new { error = "Export file not found. Re-process the report." });

        // PhysicalFile streams the file directly to the response — avoids loading it into memory.
        return PhysicalFile(path, contentType, $"Slice_Report_{report.ReportDate:yyyyMMdd}.{ext}");
    }

    // ─── Edit endpoints (Admin only) ─────────────────────────────────────────

    /// <summary>
    /// Edita una fila del Daily Global (por Pod). Solo Admin.
    /// </summary>
    [HttpPatch("{reportId}/global/{pod}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EditGlobalRow(string reportId, string pod, [FromBody] DailyGlobalRowPatch patch)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        var row = report.DailyGlobal.FirstOrDefault(g => g.Pod.Equals(pod, StringComparison.OrdinalIgnoreCase));
        if (row == null) return NotFound(new { error = $"Pod '{pod}' not found in report." });

        ApplyPatch(row, patch);
        await _reports.SaveAsync(report);
        return Ok(row);
    }

    /// <summary>
    /// Edita una fila del Daily Agent (por email del agente). Solo Admin.
    /// </summary>
    [HttpPatch("{reportId}/agent/{agentEmail}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EditAgentRow(string reportId, string agentEmail, [FromBody] DailyAgentRowPatch patch)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        var row = report.DailyAgents.FirstOrDefault(a => a.AgentEmail.Equals(agentEmail, StringComparison.OrdinalIgnoreCase));
        if (row == null) return NotFound(new { error = $"Agent '{agentEmail}' not found in report." });

        ApplyPatch(row, patch);
        await _reports.SaveAsync(report);
        return Ok(row);
    }

    /// <summary>
    /// Edita una fila del Shop Daily (por nombre del shop). Solo Admin.
    /// </summary>
    [HttpPatch("{reportId}/shop/{shopName}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> EditShopRow(string reportId, string shopName, [FromBody] ShopDailyRowPatch patch)
    {
        var report = await _reports.GetByIdAsync(reportId);
        if (report == null) return NotFound();

        var row = report.ShopDaily.FirstOrDefault(s => s.ShopName.Equals(shopName, StringComparison.OrdinalIgnoreCase));
        if (row == null) return NotFound(new { error = $"Shop '{shopName}' not found in report." });

        ApplyPatch(row, patch);
        await _reports.SaveAsync(report);
        return Ok(row);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private string GetCurrentEmail() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

    private static void ApplyPatch(DailyGlobalRow row, DailyGlobalRowPatch p)
    {
        if (p.Queued.HasValue)              row.Queued              = p.Queued.Value;
        if (p.Handled.HasValue)             row.Handled             = p.Handled.Value;
        if (p.MissedCalls.HasValue)         row.MissedCalls         = p.MissedCalls.Value;
        if (p.TransferredCalls.HasValue)    row.TransferredCalls    = p.TransferredCalls.Value;
        if (p.PctQueued.HasValue)           row.PctQueued           = p.PctQueued.Value;
        if (p.PctHandled.HasValue)          row.PctHandled          = p.PctHandled.Value;
        if (p.PctMissed.HasValue)           row.PctMissed           = p.PctMissed.Value;
        if (p.PctTransferred.HasValue)      row.PctTransferred      = p.PctTransferred.Value;
        if (p.ConvPct.HasValue)             row.ConvPct             = p.ConvPct.Value;
        if (p.OrderCount.HasValue)          row.OrderCount          = p.OrderCount.Value;
        if (p.RefundedOrders.HasValue)      row.RefundedOrders      = p.RefundedOrders.Value;
        if (p.PctOrdersWithErrors.HasValue) row.PctOrdersWithErrors = p.PctOrdersWithErrors.Value;
    }

    private static void ApplyPatch(DailyAgentRow row, DailyAgentRowPatch p)
    {
        if (p.HC.HasValue)                  row.HC                  = p.HC.Value;
        if (p.TC.HasValue)                  row.TC                  = p.TC.Value;
        if (p.NumberOfHolds.HasValue)       row.NumberOfHolds       = p.NumberOfHolds.Value;
        if (p.AvgHoldTime.HasValue)         row.AvgHoldTime         = p.AvgHoldTime.Value;
        if (p.ASA.HasValue)                 row.ASA                 = p.ASA.Value;
        if (p.AHT.HasValue)                 row.AHT                 = p.AHT.Value;
        if (p.ACW.HasValue)                 row.ACW                 = p.ACW.Value;
        if (p.PctContactsOnHold.HasValue)   row.PctContactsOnHold   = p.PctContactsOnHold.Value;
        if (p.PctSLUnder15Sec.HasValue)     row.PctSLUnder15Sec     = p.PctSLUnder15Sec.Value;
        if (p.PctTransfers.HasValue)        row.PctTransfers        = p.PctTransfers.Value;
        if (!string.IsNullOrWhiteSpace(p.Shift))          row.Shift          = p.Shift;
        if (!string.IsNullOrWhiteSpace(p.SupervisorName)) row.SupervisorName = p.SupervisorName;
    }

    private static void ApplyPatch(ShopDailyRow row, ShopDailyRowPatch p)
    {
        if (p.TotalOrders.HasValue)     row.TotalOrders     = p.TotalOrders.Value;
        if (p.RefundedOrders.HasValue)  row.RefundedOrders  = p.RefundedOrders.Value;
        if (p.ErrorRate.HasValue)       row.ErrorRate       = p.ErrorRate.Value;
        if (p.ConversionRate.HasValue)  row.ConversionRate  = p.ConversionRate.Value;
    }
}
