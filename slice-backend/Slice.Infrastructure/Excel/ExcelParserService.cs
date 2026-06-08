using System.Globalization;
using Microsoft.Extensions.Logging;
using OfficeOpenXml;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;

namespace Slice.Infrastructure.Excel;

/// <summary>
/// Parses Slice-formatted workbooks into <see cref="SliceReport"/> domain objects.
/// Accepts both Excel (.xlsx/.xls/.xlsm) and CSV files.
/// Scans every worksheet / the single CSV stream looking for three section
/// headers (case-insensitive): "Daily Global", "Daily Agent" and "Shop Daily".
/// Returns <c>null</c> if the file contains none of those sections.
/// </summary>
public sealed class ExcelParserService : IExcelParserService
{
    private readonly ILogger<ExcelParserService> _logger;

    public ExcelParserService(ILogger<ExcelParserService> logger) => _logger = logger;

    /// <inheritdoc/>
    public async Task<SliceReport?> ParseAsync(string filePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext == ".csv")
        {
            return await ParseCsvAsync(filePath, ct);
        }
        return await ParseXlsxAsync(filePath, ct);
    }

    private async Task<SliceReport?> ParseXlsxAsync(string filePath, CancellationToken ct)
    {
        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true);

        using var package = new ExcelPackage();
        await package.LoadAsync(fileStream, ct);

        var report = new SliceReport
        {
            ReportDate  = DateTime.UtcNow.Date,
            GeneratedAt = DateTime.UtcNow,
        };

        foreach (var ws in package.Workbook.Worksheets)
        {
            try { ParseWorksheet(ws, report); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse worksheet '{Name}' in {File}", ws.Name, filePath);
            }
        }

        return FinalizeReport(report, filePath);
    }

    private async Task<SliceReport?> ParseCsvAsync(string filePath, CancellationToken ct)
    {
        var rows = await ReadCsvAsync(filePath, ct);
        if (rows.Count == 0)
        {
            _logger.LogWarning("CSV file is empty: {File}", filePath);
            return null;
        }

        var report = new SliceReport
        {
            ReportDate  = DateTime.UtcNow.Date,
            GeneratedAt = DateTime.UtcNow,
        };

        try { ParseRowGrid(rows, report); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse CSV {File}", filePath);
        }

        return FinalizeReport(report, filePath);
    }

    private SliceReport? FinalizeReport(SliceReport report, string filePath)
    {
        if (report.DailyGlobal.Count == 0
            && report.DailyAgents.Count == 0
            && report.ShopDaily.Count == 0
            && report.ShopCallMetrics.Count == 0)
        {
            _logger.LogWarning("No recognizable Slice data found in {File}", filePath);
            return null;
        }
        return report;
    }

    // ─── Excel path ───────────────────────────────────────────────────────────

    /// <summary>
    /// Scans a single worksheet row-by-row looking for section header keywords,
    /// then delegates to the corresponding section parser.
    /// </summary>
    private static void ParseWorksheet(ExcelWorksheet ws, SliceReport report)
    {
        int rows = ws.Dimension?.Rows ?? 0;
        int cols = ws.Dimension?.Columns ?? 0;
        if (rows == 0 || cols == 0) return;

        var grid = new List<IList<string>>(rows);
        for (int r = 1; r <= rows; r++)
        {
            var line = new List<string>(cols);
            for (int c = 1; c <= cols; c++)
            {
                line.Add(ws.Cells[r, c].GetValue<string>()?.Trim() ?? string.Empty);
            }
            grid.Add(line);
        }
        ParseRowGrid(grid, report);
    }

    // ─── CSV path ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a CSV file with auto-detected delimiter (comma, semicolon or tab).
    /// Honors double-quoted fields with embedded delimiters and "" escapes.
    /// </summary>
    private static async Task<List<IList<string>>> ReadCsvAsync(string filePath, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var delim = DetectDelimiter(lines);
        var rows  = new List<IList<string>>(lines.Length);
        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();
            rows.Add(ParseCsvLine(line, delim));
        }
        return rows;
    }

    private static char DetectDelimiter(string[] lines)
    {
        int commas = 0, semis = 0, tabs = 0;
        foreach (var line in lines)
        {
            commas += line.Count(c => c == ',');
            semis  += line.Count(c => c == ';');
            tabs   += line.Count(c => c == '\t');
        }
        if (tabs   >= commas && tabs   >= semis  && tabs   > 0) return '\t';
        if (semis  >= commas && semis  > 0) return ';';
        return ',';
    }

    private static IList<string> ParseCsvLine(string line, char delim)
    {
        var result = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == delim) { result.Add(sb.ToString().Trim()); sb.Clear(); }
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString().Trim());
        return result;
    }

    // ─── Common section parsers (work on a row grid) ──────────────────────────

    private static void ParseRowGrid(IList<IList<string>> grid, SliceReport report)
    {
        if (TryParseShopCallMetricsPivoted(grid, report)) return;

        for (int row = 0; row < grid.Count; row++)
        {
            var cellValue = GetCell(grid, row, 0);

            if (cellValue.Contains("Daily Global", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyGlobalSection(grid, row + 2, report);
                continue;
            }
            if (cellValue.Contains("Daily Agent", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyAgentSection(grid, row + 2, report);
                continue;
            }
            if (cellValue.Contains("Shop Daily", StringComparison.OrdinalIgnoreCase))
            {
                ParseShopDailySection(grid, row + 2, report);
                continue;
            }
        }
    }

    /// <summary>
    /// Detects the pivoted shop-level call-metrics layout (one column block per week,
    /// header in row 0 says "week" and row 1 has the column names). Maps each non-empty
    /// (Shop, Pod, Week) cell block to a <see cref="ShopCallMetricsRow"/>.
    /// </summary>
    private static bool TryParseShopCallMetricsPivoted(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var firstCell = GetCell(grid, 0, 0);
        if (!firstCell.Contains("week", StringComparison.OrdinalIgnoreCase)) return false;

        // Row 1 = column-name row. Find the offsets of the canonical Shop/Pod/identifier columns.
        var headerRow = grid[1];
        int shopIdCol = -1, shopNameCol = -1, podIdCol = -1;
        for (int c = 0; c < headerRow.Count; c++)
        {
            var h = headerRow[c]?.Trim() ?? string.Empty;
            if (h.Equals("Shop ID", StringComparison.OrdinalIgnoreCase) && shopIdCol == -1) shopIdCol = c;
            else if (h.Equals("Shop Name", StringComparison.OrdinalIgnoreCase) && shopNameCol == -1) shopNameCol = c;
            else if (h.Equals("Pod ID", StringComparison.OrdinalIgnoreCase) && podIdCol == -1) podIdCol = c;
        }
        if (shopIdCol < 0 || shopNameCol < 0 || podIdCol < 0) return false;

        // Row 0 contains week-start dates. Each date column marks the start of a metric block.
        // Block layout: [date, Total, Overflow, Queue, Handled, Missed, Transferred,
        //                %Overflow, %Queued, %Handled, %Missed of queued, %Transferred] = 12 cols.
        var dateRow = grid[0];
        var weekStarts = new List<(int col, DateTime week)>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            var s = dateRow[c]?.Trim() ?? string.Empty;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            {
                weekStarts.Add((c, d.Date));
            }
        }
        if (weekStarts.Count == 0) return false;

        // Walk each data row (skip header rows 0 and 1).
        for (int r = 2; r < grid.Count; r++)
        {
            var shopId   = GetCell(grid, r, shopIdCol);
            var shopName = GetCell(grid, r, shopNameCol);
            var podId    = GetCell(grid, r, podIdCol);
            if (string.IsNullOrWhiteSpace(shopId) || string.IsNullOrWhiteSpace(shopName)) continue;

            for (int w = 0; w < weekStarts.Count; w++)
            {
                var (dateCol, week) = weekStarts[w];
                // Metrics start one column to the right of the date.
                int m0 = dateCol + 1;

                int totalCalls = GetInt(grid, r, m0);
                // Skip empty weeks (no calls at all for this shop).
                if (totalCalls == 0 && GetString(grid, r, m0) == string.Empty) continue;

                report.ShopCallMetrics.Add(new ShopCallMetricsRow
                {
                    WeekStart        = week,
                    ShopId           = shopId,
                    ShopName         = shopName,
                    PodId            = podId,
                    TotalCalls       = totalCalls,
                    OverflowCalls    = GetInt(grid, r, m0 + 1),
                    QueueCalls       = GetInt(grid, r, m0 + 2),
                    HandledCalls     = GetInt(grid, r, m0 + 3),
                    MissedCalls      = GetInt(grid, r, m0 + 4),
                    TransferredCalls = GetInt(grid, r, m0 + 5),
                    PctOverflow      = GetDouble(grid, r, m0 + 6),
                    PctQueued        = GetDouble(grid, r, m0 + 7),
                    PctHandled       = GetDouble(grid, r, m0 + 8),
                    PctMissedOfQueued= GetDouble(grid, r, m0 + 9),
                    PctTransferred   = GetDouble(grid, r, m0 + 10),
                });
            }
        }

        return report.ShopCallMetrics.Count > 0;
    }

    /// <summary>
    /// Reads the Daily Global table starting at <paramref name="startRow"/> (0-indexed).
    /// Stops at the first row whose Pod column doesn't start with "ES-".
    /// </summary>
    private static void ParseDailyGlobalSection(IList<IList<string>> grid, int startRow, SliceReport report)
    {
        for (int r = startRow + 1; r < grid.Count; r++)
        {
            var pod = GetCell(grid, r, 0);
            if (string.IsNullOrWhiteSpace(pod) || !pod.StartsWith("ES-", StringComparison.OrdinalIgnoreCase))
                break;

            report.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod                 = pod,
                Queued              = GetInt(grid, r, 1),
                Handled             = GetInt(grid, r, 2),
                MissedCalls         = GetInt(grid, r, 3),
                TransferredCalls    = GetInt(grid, r, 4),
                PctQueued           = GetDouble(grid, r, 5),
                PctHandled          = GetDouble(grid, r, 6),
                PctMissed           = GetDouble(grid, r, 7),
                PctTransferred      = GetDouble(grid, r, 8),
                ConvPct             = GetDouble(grid, r, 9),
                OrderCount          = GetInt(grid, r, 10),
                RefundedOrders      = GetInt(grid, r, 11),
                PctOrdersWithErrors = GetDouble(grid, r, 12),
            });
        }
    }

    /// <summary>
    /// Reads the Daily Agent table starting at <paramref name="startRow"/> (0-indexed).
    /// Tracks the current pod and supervisor from "POD" header rows
    /// and attaches them to every agent row that follows.
    /// </summary>
    private static void ParseDailyAgentSection(IList<IList<string>> grid, int startRow, SliceReport report)
    {
        string currentPod        = string.Empty;
        string currentSupervisor = string.Empty;

        for (int r = startRow; r < grid.Count; r++)
        {
            var col1 = GetCell(grid, r, 0);
            var col2 = GetCell(grid, r, 1);

            if (col1.Equals("POD", StringComparison.OrdinalIgnoreCase))
            {
                currentPod        = GetCell(grid, r, 2);
                currentSupervisor = GetCell(grid, r, 11);
                continue;
            }

            if (col1.Equals("Agent", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(col1) && string.IsNullOrWhiteSpace(col2)) continue;

            if (col1.Contains('@'))
            {
                report.DailyAgents.Add(new DailyAgentRow
                {
                    Pod                = currentPod,
                    SupervisorName     = currentSupervisor,
                    AgentEmail         = col1,
                    HC                 = GetInt(grid, r, 1),
                    TC                 = GetInt(grid, r, 2),
                    NumberOfHolds      = GetInt(grid, r, 3),
                    AvgHoldTime        = GetDouble(grid, r, 4),
                    ASA                = GetDouble(grid, r, 5),
                    AHT                = GetDouble(grid, r, 6),
                    ACW                = GetDouble(grid, r, 7),
                    PctContactsOnHold  = GetDouble(grid, r, 8),
                    PctSLUnder15Sec    = GetDouble(grid, r, 9),
                    PctTransfers       = GetDouble(grid, r, 10),
                    Shift              = GetString(grid, r, 11),
                });
            }
        }
    }

    /// <summary>
    /// Reads the Shop Daily table starting at <paramref name="startRow"/> (0-indexed).
    /// Stops at the first blank shop-name row.
    /// </summary>
    private static void ParseShopDailySection(IList<IList<string>> grid, int startRow, SliceReport report)
    {
        for (int r = startRow + 1; r < grid.Count; r++)
        {
            var shop = GetCell(grid, r, 0);
            if (string.IsNullOrWhiteSpace(shop)) break;

            report.ShopDaily.Add(new ShopDailyRow
            {
                ShopName       = shop,
                TotalOrders    = GetInt(grid, r, 1),
                RefundedOrders = GetInt(grid, r, 2),
                ErrorRate      = GetDouble(grid, r, 3),
                ConversionRate = GetDouble(grid, r, 4),
            });
        }
    }

    // ─── Cell-value helpers ──────────────────────────────────────────────────

    private static string GetCell(IList<IList<string>> grid, int row, int col)
    {
        if (row < 0 || row >= grid.Count) return string.Empty;
        var line = grid[row];
        if (col < 0 || col >= line.Count) return string.Empty;
        return line[col]?.Trim() ?? string.Empty;
    }

    private static string GetString(IList<IList<string>> grid, int row, int col) => GetCell(grid, row, col);

    private static int GetInt(IList<IList<string>> grid, int row, int col)
    {
        var s = GetCell(grid, row, col);
        if (string.IsNullOrEmpty(s)) return 0;
        s = s.Replace(",", "").Replace("%", "").Trim();
        return int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    private static double GetDouble(IList<IList<string>> grid, int row, int col)
    {
        var s = GetCell(grid, row, col);
        if (string.IsNullOrEmpty(s)) return 0.0;
        s = s.Replace(",", "").Replace("%", "").Trim();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0.0;
    }
}
