using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Excel;

public sealed class ExcelParserService : IExcelParserService
{
    private readonly ILogger<ExcelParserService> _logger;

    public ExcelParserService(ILogger<ExcelParserService> logger)
    {
        _logger = logger;
        ExcelPackage.License.SetNonCommercialPersonal("Slice");
    }

    public async Task<SliceReport?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true);

        using var package = new ExcelPackage();
        await package.LoadAsync(fileStream, ct);

        var report = new SliceReport
        {
            ReportDate = DateTime.UtcNow.Date,
            GeneratedAt = DateTime.UtcNow
        };

        foreach (var ws in package.Workbook.Worksheets)
        {
            try { ParseWorksheet(ws, report); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse worksheet '{Name}' in {File}", ws.Name, filePath);
            }
        }

        if (report.DailyGlobal.Count == 0 && report.DailyAgents.Count == 0)
        {
            _logger.LogWarning("No recognizable Slice data found in {File}", filePath);
            return null;
        }

        return report;
    }

    private static void ParseWorksheet(ExcelWorksheet ws, SliceReport report)
    {
        int rows = ws.Dimension?.Rows ?? 0;
        int cols = ws.Dimension?.Columns ?? 0;
        if (rows == 0 || cols == 0) return;

        for (int row = 1; row <= rows; row++)
        {
            var cellValue = GetString(ws, row, 1);

            if (cellValue.Contains("Daily Global", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyGlobalSection(ws, row + 2, rows, report);
                continue;
            }

            if (cellValue.Contains("Daily Agent", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyAgentSection(ws, row + 2, rows, report);
                continue;
            }

            if (cellValue.Contains("Shop Daily", StringComparison.OrdinalIgnoreCase))
            {
                ParseShopDailySection(ws, row + 2, rows, report);
                continue;
            }
        }
    }

    private static void ParseDailyGlobalSection(ExcelWorksheet ws, int startRow, int maxRow, SliceReport report)
    {
        // Header row is startRow; data starts at startRow+1
        for (int r = startRow + 1; r <= maxRow; r++)
        {
            var pod = GetString(ws, r, 1);
            if (string.IsNullOrWhiteSpace(pod) || pod.StartsWith("ES-", StringComparison.OrdinalIgnoreCase) == false)
                break;

            report.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod = pod,
                Queued = GetInt(ws, r, 2),
                Handled = GetInt(ws, r, 3),
                MissedCalls = GetInt(ws, r, 4),
                TransferredCalls = GetInt(ws, r, 5),
                PctQueued = GetDouble(ws, r, 6),
                PctHandled = GetDouble(ws, r, 7),
                PctMissed = GetDouble(ws, r, 8),
                PctTransferred = GetDouble(ws, r, 9),
                ConvPct = GetDouble(ws, r, 10),
                OrderCount = GetInt(ws, r, 11),
                RefundedOrders = GetInt(ws, r, 12),
                PctOrdersWithErrors = GetDouble(ws, r, 13),
            });
        }
    }

    private static void ParseDailyAgentSection(ExcelWorksheet ws, int startRow, int maxRow, SliceReport report)
    {
        string currentPod = string.Empty;
        string currentSupervisor = string.Empty;

        for (int r = startRow; r <= maxRow; r++)
        {
            var col1 = GetString(ws, r, 1);
            var col2 = GetString(ws, r, 2);

            // Pod header row: "POD" in col B, pod name in col C
            if (col1.Equals("POD", StringComparison.OrdinalIgnoreCase))
            {
                currentPod = GetString(ws, r, 3);
                currentSupervisor = GetString(ws, r, 12);
                continue;
            }

            // Skip header rows
            if (col1.Equals("Agent", StringComparison.OrdinalIgnoreCase)) continue;

            // Empty row = section separator
            if (string.IsNullOrWhiteSpace(col1) && string.IsNullOrWhiteSpace(col2)) continue;

            // Agent data row: col1 is email-like
            if (col1.Contains("@"))
            {
                report.DailyAgents.Add(new DailyAgentRow
                {
                    Pod = currentPod,
                    SupervisorName = currentSupervisor,
                    AgentEmail = col1,
                    HC = GetInt(ws, r, 2),
                    TC = GetInt(ws, r, 3),
                    NumberOfHolds = GetInt(ws, r, 4),
                    AvgHoldTime = GetDouble(ws, r, 5),
                    ASA = GetDouble(ws, r, 6),
                    AHT = GetDouble(ws, r, 7),
                    ACW = GetDouble(ws, r, 8),
                    PctContactsOnHold = GetDouble(ws, r, 9),
                    PctSLUnder15Sec = GetDouble(ws, r, 10),
                    PctTransfers = GetDouble(ws, r, 11),
                    Shift = GetString(ws, r, 12),
                });
            }
        }
    }

    private static void ParseShopDailySection(ExcelWorksheet ws, int startRow, int maxRow, SliceReport report)
    {
        for (int r = startRow + 1; r <= maxRow; r++)
        {
            var shop = GetString(ws, r, 1);
            if (string.IsNullOrWhiteSpace(shop)) break;

            report.ShopDaily.Add(new ShopDailyRow
            {
                ShopName = shop,
                TotalOrders = GetInt(ws, r, 2),
                RefundedOrders = GetInt(ws, r, 3),
                ErrorRate = GetDouble(ws, r, 4),
                ConversionRate = GetDouble(ws, r, 5),
            });
        }
    }

    private static string GetString(ExcelWorksheet ws, int row, int col)
        => ws.Cells[row, col].GetValue<string>()?.Trim() ?? string.Empty;

    private static int GetInt(ExcelWorksheet ws, int row, int col)
    {
        var val = ws.Cells[row, col].Value;
        return val switch
        {
            double d => (int)d,
            int i => i,
            string s when int.TryParse(s, out var n) => n,
            _ => 0
        };
    }

    private static double GetDouble(ExcelWorksheet ws, int row, int col)
    {
        var val = ws.Cells[row, col].Value;
        return val switch
        {
            double d => d,
            int i => i,
            string s when double.TryParse(s, out var n) => n,
            _ => 0.0
        };
    }
}
