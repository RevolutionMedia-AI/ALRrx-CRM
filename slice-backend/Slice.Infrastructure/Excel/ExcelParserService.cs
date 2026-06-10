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

        try { ParseRowGrid(rows, report, filePath); }
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
        ParseRowGrid(grid, report, ws.Name);
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

    private static void ParseRowGrid(IList<IList<string>> grid, SliceReport report, string sourceHint)
    {
        // 1. Detect well-known layouts by name/header and map to a section.
        //    The "POD_PIVOTED" family wins first because it's the most
        //    distinctive (it has dates in row 0 and "Pod ID" in row 1).
        if (TryParsePodLevelPivoted(grid, report))                return;
        if (TryParseAgentLevelWeeklyPivoted(grid, report))         return;
        if (TryParseAgentLevelPivoted(grid, report))               return;
        if (TryParseShopLevelPivoted(grid, report))                 return;
        if (TryParseShopLevelOrdersWeekly(grid, report))            return;
        if (TryParseShopLevelOrderMetrics(grid, report))            return;
        if (TryParseItemsRemovedByShippingType(grid, report))       return;
        if (TryParseOpenFoodItemsByProduct(grid, report))           return;
        if (TryParsePodsGeneratingOverflow(grid, report))           return;
        if (TryParsePodsHelpingOverflow(grid, report))              return;
        if (TryParseExternalOverflowDaily(grid, report))            return;
        if (TryParseCallMetricsByShopType(grid, report))            return;
        if (TryParseOverflow15Min(grid, report))                    return;
        if (TryParseOverflowHourly(grid, report))                   return;
        if (TryParseOrdersFromCalls(grid, report))                  return;
        if (TryParseOrderFromCalls(grid, report))                   return;
        if (TryParseShopCallMetricsPivoted(grid, report))            return;

        // 2. Fallback to the classic "Daily Global / Daily Agent / Shop Daily"
        //    section-header format (used by the Excel template). We scan every
        //    cell in each row (not just column 0) because the section title can
        //    be centered above a block of columns while column A is used as a
        //    visual margin and left blank. The two-tier header pattern is:
        //      title row:    "Global" / "Agent" / "Shop" (centered, 1 cell only)
        //      column row:   "Pod" / "Queued" / ...  / "Pod - Shops" / ...
        //      data row:     "ES-12" / 6132 / ...   / "ALL" / "EXTERNAL..." / ...
        //    The legacy "Daily Global" / "Shop Daily" title format is still
        //    supported, but the new shorter title format takes priority.
        for (int row = 0; row < grid.Count; row++)
        {
            string rowText = string.Join(' ', Enumerable.Range(0, Math.Min(20, grid[row].Count))
                .Select(c => GetCell(grid, row, c)));

            if (rowText.Contains("Daily Global", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyGlobalSection(grid, row + 2, report);
                continue;
            }
            if (rowText.Contains("Daily Agent", StringComparison.OrdinalIgnoreCase))
            {
                ParseDailyAgentSection(grid, row + 2, report);
                continue;
            }
            if (rowText.Contains("Shop Daily", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy: the header row IS the row that contains "Shop Daily"
                // and the column labels. Data starts one row below.
                ParseShopDailySection(grid, row + 1, report);
                continue;
            }

            // Shorter title format: "Global" or "Agent" or "Shop" alone, in a
            // row where the only non-empty cell is the title itself. The next
            // row is the column-header row, and the one after that is the data.
            string firstNonEmpty = Enumerable.Range(0, grid[row].Count)
                .Select(c => GetCell(grid, row, c))
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? string.Empty;
            int nonEmptyCount = Enumerable.Range(0, grid[row].Count)
                .Count(c => !string.IsNullOrWhiteSpace(GetCell(grid, row, c)));

            if (nonEmptyCount == 1)
            {
                if (firstNonEmpty.Equals("Global", StringComparison.OrdinalIgnoreCase))
                {
                    ParseDailyGlobalSection(grid, row + 2, report);
                    continue;
                }
                if (firstNonEmpty.Equals("Agent", StringComparison.OrdinalIgnoreCase))
                {
                    ParseDailyAgentSection(grid, row + 2, report);
                    continue;
                }
                if (firstNonEmpty.Equals("Shop", StringComparison.OrdinalIgnoreCase))
                {
                    // Two-tier header: title row -> column-header row -> data rows.
                    // Pass the column-header row; the parser will read from row+1.
                    ParseShopDailySection(grid, row + 1, report);
                    continue;
                }
            }
        }

        // Suppress unused-warning on the hint parameter (kept for future telemetry).
        _ = sourceHint;
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
    /// <summary>
    /// Reads the Shop block starting at <paramref name="headerRow"/> (0-indexed).
    /// The user's Excel uses a 18-column hybrid header that combines call
    /// metrics (cols 3-13) and order metrics (cols 14-17) in the same row:
    ///   0: Pod - Shops   1: Shop ID   2: Shop Name
    ///   3: Total Calls  4: Overflow  5: Queued  6: Handle  7: Missed Calls
    ///   8: Transferred Calls  9: %Overflow  10: %Queued  11: %Handled
    ///  12: %missed  13: %Transferred
    ///  14: Order Count  15: Conv %  16: Refunded Orders  17: % Orders with errors
    /// We populate both <c>ShopCallMetrics</c> (call side) and <c>ShopDaily</c>
    /// (orders side) for every data row so the export can show them separately
    /// and the backfill can use ShopName as a bridge between the two collections.
    /// </summary>
    private static void ParseShopDailySection(IList<IList<string>> grid, int headerRow, SliceReport report)
    {
        if (headerRow < 0 || headerRow >= grid.Count) return;
        var header = grid[headerRow];
        // Sanity check: at least one of the expected Shop headers should be
        // present in this row. If not, this is the wrong row.
        bool looksLikeShopHeader = header.Any(c => c?.Trim().Equals("Pod - Shops", StringComparison.OrdinalIgnoreCase) == true
                                                || c?.Trim().Equals("Shop ID", StringComparison.OrdinalIgnoreCase) == true);
        if (!looksLikeShopHeader) return;

        int dataStart = headerRow + 1;
        for (int r = dataStart; r < grid.Count; r++)
        {
            var pod     = GetCell(grid, r, 0);
            var shopId  = GetCell(grid, r, 1);
            var shopName= GetCell(grid, r, 2);
            if (string.IsNullOrWhiteSpace(shopId) && string.IsNullOrWhiteSpace(shopName)) break;
            // The legacy "shop daily" layout used a single-column shop label in col 0
            // and 4 metrics. If col 0 is a non-ES-X label (i.e. a shop name) and the
            // other columns are empty, treat it as legacy and keep the old behavior.
            if (!string.IsNullOrWhiteSpace(pod)
                && !pod.StartsWith("ES-", StringComparison.OrdinalIgnoreCase)
                && !pod.Equals("ALL", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(shopId)
                && string.IsNullOrWhiteSpace(shopName))
            {
                report.ShopDaily.Add(new ShopDailyRow
                {
                    ShopName       = pod,
                    TotalOrders    = GetInt(grid, r, 1),
                    RefundedOrders = GetInt(grid, r, 2),
                    ErrorRate      = GetDouble(grid, r, 3),
                    ConversionRate = GetDouble(grid, r, 4),
                });
                continue;
            }

            // Hybrid 18-col layout: populate both collections.
            int total     = GetInt(grid, r, 3);
            int overflow  = GetInt(grid, r, 4);
            int queued    = GetInt(grid, r, 5);
            int handled   = GetInt(grid, r, 6);
            int missed    = GetInt(grid, r, 7);
            int transfer  = GetInt(grid, r, 8);
            double pctOv  = GetDouble(grid, r, 9);
            double pctQu  = GetDouble(grid, r, 10);
            double pctHa  = GetDouble(grid, r, 11);
            double pctMi  = GetDouble(grid, r, 12);
            double pctTr  = GetDouble(grid, r, 13);
            int orders    = GetInt(grid, r, 14);
            double conv   = GetDouble(grid, r, 15);
            int refunded  = GetInt(grid, r, 16);
            double pctErr = GetDouble(grid, r, 17);

            // The "ALL" pod + the EXTERNAL_OVERFLOW_DAILY virtual row are
            // emitted by the CSV parser, so skip them when they appear in the
            // Excel to avoid duplicating the same row in ShopCallMetrics.
            bool isExternal = string.Equals(shopId, "EXTERNAL_OVERFLOW_DAILY", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(shopName, "EXTERNAL_OVERFLOW_DAILY", StringComparison.OrdinalIgnoreCase);
            if (!isExternal && (total + overflow + queued + handled + missed + transfer > 0))
            {
                report.ShopCallMetrics.Add(new ShopCallMetricsRow
                {
                    WeekStart         = report.ReportDate,
                    ShopId            = shopId,
                    ShopName          = shopName,
                    PodId             = pod,
                    TotalCalls        = total,
                    OverflowCalls     = overflow,
                    QueueCalls        = queued,
                    HandledCalls      = handled,
                    MissedCalls       = missed,
                    TransferredCalls  = transfer,
                    PctOverflow       = pctOv,
                    PctQueued         = pctQu,
                    PctHandled        = pctHa,
                    PctMissedOfQueued = pctMi,
                    PctTransferred    = pctTr,
                });
            }

            if (orders + refunded > 0 || pctErr > 0 || conv > 0)
            {
                report.ShopDaily.Add(new ShopDailyRow
                {
                    ShopName       = string.IsNullOrWhiteSpace(shopName) ? shopId : shopName,
                    ShopId         = shopId,
                    TotalOrders    = orders,
                    RefundedOrders = refunded,
                    ErrorRate      = pctErr,
                    ConversionRate = conv,
                });
            }
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

    // ─── Per-CSV-shape auto-detectors ──────────────────────────────────────
    //
    // Each detector returns true if it recognized the layout and populated the
    // corresponding report section(s). The detectors are ordered roughly by
    // specificity so the more distinctive layouts win first.

    /// <summary>
    /// <c>pod_level_-_call_metrics.csv</c>: one row per (Pod, Day). Two variants:
    ///   - DAILY  (8 cols per day): [date, Total, Overflow, Queue, Handled, Missed, Transferred, %×4]
    ///   - WEEKLY (12 cols per day): [date, Total, Overflow, Queue, Handled, Missed, Transferred, Avg Speed to Answer, %×4]
    /// We detect the block width dynamically by counting columns between two
    /// adjacent date columns. Adds to <c>DailyGlobal</c>.
    /// </summary>
    private static bool TryParsePodLevelPivoted(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid.Count > 1 ? grid[1] : grid[0];
        int podIdCol = FindColumn(headerRow, "Pod ID");
        if (podIdCol < 0) return false;

        // Find every "Total Calls" column in the header. Each occurrence
        // marks the start of a new day/week block. The metrics are at
        // fixed offsets within the block (column names repeat), so this
        // works for both the 11-col daily variant and the 14-col weekly
        // variant without hard-coding blockWidth.
        var totalCols = new List<int>();
        for (int c = 0; c < headerRow.Count; c++)
        {
            if (headerRow[c]?.Trim().Equals("Total Calls", StringComparison.OrdinalIgnoreCase) == true)
            {
                totalCols.Add(c);
            }
        }
        if (totalCols.Count < 1) return false;

        // Block width = distance between the first two "Total Calls" columns
        // (or 11 if there's only one block, e.g. the daily file with 14
        // days where the second block is just another date column).
        int blockWidth = totalCols.Count >= 2
            ? totalCols[1] - totalCols[0]
            : 11;

        // Read the column offsets of each metric WITHIN the first block
        // (the block repeats the same names, so we only need to scan
        // once). If a metric is missing in the first block (e.g. weekly
        // adds extra % columns), its offset is -1 and we skip it.
        int firstBlockStart = totalCols[0]; // first "Total Calls" col
        int firstDateCol    = firstBlockStart - 1; // date is 1 col before
        int blockEnd        = firstBlockStart + blockWidth;

        int OffsetOf(string label)
        {
            for (int c = firstBlockStart; c < blockEnd; c++)
            {
                if (headerRow[c]?.Trim().Equals(label, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return c - firstDateCol; // offset relative to date col
                }
            }
            return -1;
        }

        int totalOff    = OffsetOf("Total Calls");
        int overflowOff = OffsetOf("Overflow Calls");
        int queueOff    = OffsetOf("Queue Calls");
        int handledOff  = OffsetOf("Handled Calls");
        int missedOff   = OffsetOf("Missed Calls");
        int transferOff = OffsetOf("Transferred Calls");

        if (totalOff < 0 || overflowOff < 0 || queueOff < 0 ||
            handledOff < 0 || missedOff < 0 || transferOff < 0)
        {
            return false;
        }

        int pctQueuedOff   = OffsetOf("%Queued of total calls");
        int pctHandledOff  = OffsetOf("%Handled of total calls");
        int pctMissedOff   = OffsetOf("%Missed of queued calls");

        // Build list of date columns (one per block) by reading the date row.
        // For each POD, we only keep the row from the MOST RECENT date so
        // we don't sum accumulated days/weeks (which produces nonsense for
        // percentages).
        var dateRow = grid[0];
        var blockDates = new List<DateTime?>();
        foreach (var tc in totalCols)
        {
            int dateColIdx = tc - 1;
            if (dateColIdx >= 0 && dateColIdx < dateRow.Count &&
                DateTime.TryParse(dateRow[dateColIdx]?.Trim() ?? string.Empty,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            {
                blockDates.Add(d.Date);
            }
            else
            {
                blockDates.Add(null);
            }
        }

        int mostRecentIdx = blockDates.Count - 1;
        DateTime? maxDate = null;
        for (int i = 0; i < blockDates.Count; i++)
        {
            if (blockDates[i].HasValue && (!maxDate.HasValue || blockDates[i]!.Value > maxDate.Value))
            {
                maxDate = blockDates[i];
                mostRecentIdx = i;
            }
        }

        // For each POD, read metrics from the most recent block only.
        for (int r = 2; r < grid.Count; r++)
        {
            var pod = GetCell(grid, r, podIdCol);
            if (string.IsNullOrWhiteSpace(pod) ||
                !pod.StartsWith("ES-", StringComparison.OrdinalIgnoreCase)) continue;

            int bStart = totalCols[mostRecentIdx] - 1; // date col of that block

            int total    = GetInt(grid, r, bStart + totalOff);
            int overflow = GetInt(grid, r, bStart + overflowOff);
            int queue    = GetInt(grid, r, bStart + queueOff);
            int handled  = GetInt(grid, r, bStart + handledOff);
            int missed   = GetInt(grid, r, bStart + missedOff);
            int transfer = GetInt(grid, r, bStart + transferOff);

            if (total == 0 && handled == 0) continue;

            // Read percentages from CSV; if absent, recalculate from volumes.
            double pctQueued = pctQueuedOff >= 0
                ? GetDouble(grid, r, bStart + pctQueuedOff)
                : (total > 0 ? (double)queue / total * 100 : 0);
            double pctHandled = pctHandledOff >= 0
                ? GetDouble(grid, r, bStart + pctHandledOff)
                : (total > 0 ? (double)handled / total * 100 : 0);
            double pctMissed = pctMissedOff >= 0
                ? GetDouble(grid, r, bStart + pctMissedOff)
                : (queue > 0 ? (double)missed / queue * 100 : 0);

            report.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod              = pod,
                Queued           = queue,
                Handled          = handled,
                MissedCalls      = missed,
                TransferredCalls = transfer,
                PctQueued        = pctQueued,
                PctHandled       = pctHandled,
                PctMissed        = pctMissed,
                PctTransferred   = total > 0 ? (double)transfer / total * 100 : 0,
                // Order metrics (OrderCount, RefundedOrders, %OrdersWithErrors)
                // are populated later from ShopDaily in the export consumer.
            });
        }
        return report.DailyGlobal.Count > 0;
    }

    /// <summary>
    /// <c>agent_level_metrics_-_daily.csv</c>: one row per (Agent, Pod). Eight
    /// metric columns per daily block (HC, TC, Holds, AvgHold, ASA, AHT, ACW, %OnHold,
    /// %SL<15, %Transfers). Adds to <c>DailyAgents</c>.
    /// </summary>
    private static bool TryParseAgentLevelPivoted(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var headerRow = grid[1];
        int agentCol = FindColumn(headerRow, "Agent Username");
        int routingCol = FindColumn(headerRow, "Agent Routing Profile Name");
        if (agentCol < 0) return false;

        var dateRow = grid[0];
        var dayStarts = new List<int>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            if (DateTime.TryParse(dateRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                dayStarts.Add(c);
            }
        }
        if (dayStarts.Count == 0) return false;

        int blockWidth = 9; // HC, TC, Holds, AvgHold, ASA, AHT, ACW, %OnHold, %SL<15, %Transfers
        for (int r = 2; r < grid.Count; r++)
        {
            var agent = GetCell(grid, r, agentCol);
            if (string.IsNullOrWhiteSpace(agent) || !agent.Contains('@')) continue;

            // Extract Pod and supervisor from the routing profile (ES-12-JUNIOR → ES-12 / JUNIOR).
            var routing = routingCol >= 0 ? GetCell(grid, r, routingCol) : string.Empty;
            var (pod, _) = SplitRoutingProfile(routing);

            int hc = 0, tc = 0, holds = 0, trans = 0;
            double avgHold = 0, asa = 0, aht = 0, acw = 0, pctHold = 0, pctSL = 0, pctTrans = 0;
            int blockCount = 0;
            for (int c = 0; c < dayStarts.Count; c++)
            {
                int m0 = dayStarts[c] + 1;
                int hcDay = GetInt(grid, r, m0);
                if (hcDay == 0 && GetString(grid, r, m0) == string.Empty) continue;
                hc        += hcDay;
                tc        += GetInt(grid, r, m0 + 1);
                holds     += GetInt(grid, r, m0 + 2);
                avgHold   += GetDouble(grid, r, m0 + 3);
                asa       += GetDouble(grid, r, m0 + 4);
                aht       += GetDouble(grid, r, m0 + 5);
                acw       += GetDouble(grid, r, m0 + 6);
                pctHold   += GetDouble(grid, r, m0 + 7);
                pctSL     += GetDouble(grid, r, m0 + 8);
                trans     += GetInt(grid, r, m0 + 9);
                pctTrans  += GetDouble(grid, r, m0 + 9);
                blockCount++;
                if (blockWidth <= 0) break;
            }
            if (blockCount == 0) continue;

            report.DailyAgents.Add(new DailyAgentRow
            {
                Pod                = pod,
                AgentEmail         = agent,
                HC                 = hc,
                TC                 = tc,
                NumberOfHolds      = holds,
                AvgHoldTime        = SafeAvg(avgHold, blockCount),
                ASA                = SafeAvg(asa, blockCount),
                AHT                = SafeAvg(aht, blockCount),
                ACW                = SafeAvg(acw, blockCount),
                PctContactsOnHold  = SafeAvg(pctHold, blockCount),
                PctSLUnder15Sec    = SafeAvg(pctSL, blockCount),
                PctTransfers       = SafeAvg(pctTrans, blockCount),
            });
        }
        return report.DailyAgents.Count > 0;
    }

    /// <summary>
    /// Splits "ES-12-JUNIOR" into ("ES-12", "JUNIOR"). Returns empty strings
    /// when the input does not match the expected pattern.
    /// </summary>
    private static (string pod, string tier) SplitRoutingProfile(string routing)
    {
        if (string.IsNullOrWhiteSpace(routing)) return (string.Empty, string.Empty);
        var parts = routing.Split('-');
        if (parts.Length < 3) return (routing, string.Empty);
        return ($"{parts[0]}-{parts[1]}", parts[2]);
    }

    private static double SafeAvg(double total, int n) => n == 0 ? 0.0 : total / n;

    /// <summary>
    /// Returns the first column index whose header matches <paramref name="needle"/>
    /// (case-insensitive exact match). -1 if not found.
    /// </summary>
    private static int FindColumn(IList<string> headerRow, string needle)
    {
        for (int c = 0; c < headerRow.Count; c++)
        {
            if (headerRow[c]?.Trim().Equals(needle, StringComparison.OrdinalIgnoreCase) == true) return c;
        }
        return -1;
    }

    /// <summary>
    /// <c>_order_from_calls.csv</c>: simple layout, no pivoting.
    /// Columns: Date, Total Calls, Distinct Customers, Order Count, % from calls, % from customers.
    /// Maps to <c>DailyGlobal</c> as "phantom pod ORDERS" (so the user can see the
    /// trend in the same chart) or to <c>ShopDaily</c> as a single aggregate row.
    /// We map to <c>ShopDaily</c> because the columns are shop-level aggregates.
    /// </summary>
    private static bool TryParseOrderFromCalls(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int orderCountCol = FindColumn(headerRow, "Order Count");
        int totalCallsCol  = FindColumn(headerRow, "Total Calls");
        if (orderCountCol < 0 || totalCallsCol < 0) return false;

        int totalOrders = 0, totalCalls = 0, rows = 0;
        for (int r = 1; r < grid.Count; r++)
        {
            int orderCount = GetInt(grid, r, orderCountCol);
            int rowTotal   = GetInt(grid, r, totalCallsCol);
            if (orderCount == 0 && rowTotal == 0) continue;
            totalOrders += orderCount;
            totalCalls  += rowTotal;
            rows++;
        }
        if (rows == 0) return false;

        report.ShopDaily.Add(new ShopDailyRow
        {
            ShopName       = "ORDERS_FROM_CALLS",
            TotalOrders    = totalOrders,
            RefundedOrders = 0,
            ErrorRate      = 0,
            ConversionRate = totalCalls == 0 ? 0 : Math.Round((double)totalOrders / totalCalls * 100.0, 1),
        });
        return true;
    }

    /// <summary>
    /// <c>shop_level_-_call_metrics.csv</c> pivoted layout. Two variants:
    ///   - DAILY  (11 cols per day after the date): Total, Overflow, Queue, Handled,
    ///     Missed, Transferred, %×4
    ///   - WEEKLY (same 11 cols, just repeated more times; no "Live on Phone Date")
    /// In both variants the header row contains "Shop ID" + "Shop Name" + "Pod ID"
    /// + "Total Calls" and the date row sits at the same column index as
    /// "Total Calls" minus one.
    /// </summary>
    private static bool TryParseShopLevelPivoted(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var headerRow = grid[1];
        int shopIdCol    = FindColumn(headerRow, "Shop ID");
        int shopNameCol  = FindColumn(headerRow, "Shop Name");
        int podIdCol     = FindColumn(headerRow, "Pod ID");
        int liveOnCol    = FindColumn(headerRow, "Live on Phone Date");
        int totalCallsCol = FindColumn(headerRow, "Total Calls");
        if (shopIdCol < 0 || shopNameCol < 0 || podIdCol < 0 || totalCallsCol < 0) return false;

        // Block = 11 metric cols: Total..Transferred..%×4.
        const int blockWidth = 11;

        for (int r = 2; r < grid.Count; r++)
        {
            var shopId   = GetCell(grid, r, shopIdCol);
            var shopName = GetCell(grid, r, shopNameCol);
            var podId    = GetCell(grid, r, podIdCol);
            if (string.IsNullOrWhiteSpace(shopId) || string.IsNullOrWhiteSpace(shopName)) continue;

            // The first date column (where the weekly metrics start) is totalCallsCol
            // minus one (the "Live on Phone Date" column right before the first metric).
            int firstBlockStart = totalCallsCol - 1;

            for (int offset = 0; offset < headerRow.Count - firstBlockStart && offset < 1000 * blockWidth; offset += blockWidth)
            {
                int dateCol = firstBlockStart + offset;
                int total = GetInt(grid, r, dateCol + 1);
                if (total == 0 && GetString(grid, r, dateCol + 1) == string.Empty) continue;

                DateTime weekStart = DateTime.UtcNow.Date;
                if (liveOnCol >= 0)
                {
                    DateTime.TryParse(GetCell(grid, r, liveOnCol + offset),
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out weekStart);
                }

                report.ShopCallMetrics.Add(new ShopCallMetricsRow
                {
                    WeekStart        = weekStart,
                    ShopId           = shopId,
                    ShopName         = shopName,
                    PodId            = podId,
                    TotalCalls       = total,
                    OverflowCalls    = GetInt(grid, r, dateCol + 2),
                    QueueCalls       = GetInt(grid, r, dateCol + 3),
                    HandledCalls     = GetInt(grid, r, dateCol + 4),
                    MissedCalls      = GetInt(grid, r, dateCol + 5),
                    TransferredCalls = GetInt(grid, r, dateCol + 6),
                    PctOverflow      = GetDouble(grid, r, dateCol + 7),
                    PctQueued        = GetDouble(grid, r, dateCol + 8),
                    PctHandled       = GetDouble(grid, r, dateCol + 9),
                    PctMissedOfQueued= GetDouble(grid, r, dateCol + 10),
                    PctTransferred   = GetDouble(grid, r, dateCol + 11),
                });
            }
        }
        return report.ShopCallMetrics.Count > 0;
    }

    /// <summary>
    /// <c>shop_level_-_order_metrics.csv</c> pivoted by date with 5 columns per
    /// day (Orders, Refunded, Removed Items, Removed Count, % errors). Adds a
    /// <c>ShopDaily</c> row per shop.
    /// </summary>
    private static bool TryParseShopLevelOrderMetrics(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int shopIdCol  = FindColumn(headerRow, "Shop ID");
        int ordersCol  = FindColumn(headerRow, "Orders Count");
        int refundCol  = FindColumn(headerRow, "Refunded Orders");
        int errorsCol  = FindColumn(headerRow, "%orders with errors");
        if (shopIdCol < 0 || ordersCol < 0) return false;

        // Daily block width = 5.
        const int blockWidth = 5;
        int firstBlockStart = ordersCol;
        if (refundCol > 0) firstBlockStart = refundCol;
        if (firstBlockStart <= shopIdCol) return false;

        for (int r = 1; r < grid.Count; r++)
        {
            var shopId = GetCell(grid, r, shopIdCol);
            if (string.IsNullOrWhiteSpace(shopId)) continue;

            int total = 0, refunded = 0;
            double pctErrors = 0;
            int blocks = 0;
            for (int offset = 0;
                 offset < headerRow.Count - firstBlockStart && offset < 1000 * blockWidth;
                 offset += blockWidth)
            {
                int o  = GetInt(grid, r, firstBlockStart + offset);
                int r2 = refundCol >= 0 ? GetInt(grid, r, refundCol + offset) : 0;
                double e = errorsCol >= 0 ? GetDouble(grid, r, errorsCol + offset) : 0;
                if (o == 0 && r2 == 0) continue;
                total    += o;
                refunded += r2;
                pctErrors += e;
                blocks++;
            }
            if (blocks == 0) continue;

            report.ShopDaily.Add(new ShopDailyRow
            {
                ShopName       = shopId,
                ShopId         = shopId,
                TotalOrders    = total,
                RefundedOrders = refunded,
                ErrorRate      = SafeAvg(pctErrors, blocks),
                ConversionRate = 0,
            });
        }
        return report.ShopDaily.Count > 0;
    }

    /// <summary>
    /// <c>call_metrics_by_shop_type_*.csv</c>: two rows (new / established / total)
    /// with 11 metrics per day. We expose the TOTAL row as <c>DailyGlobal</c>
    /// entries keyed by pod "ALL" (so it appears in the same chart as the pods).
    /// </summary>
    private static bool TryParseCallMetricsByShopType(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int typeCol    = FindColumn(headerRow, "Shop Type");
        int shopsCol   = FindColumn(headerRow, "#shops");
        int totalCol   = FindColumn(headerRow, "Total Calls");
        int missedCol  = FindColumn(headerRow, "Missed Calls");
        int queueCol   = FindColumn(headerRow, "Queue Calls");
        int handledCol = FindColumn(headerRow, "Handled Calls");
        int transCol   = FindColumn(headerRow, "Transferred Calls");
        if (typeCol < 0 || totalCol < 0) return false;

        // The block width is the distance between two adjacent #shops columns.
        int blockWidth = 11;
        var secondShops = -1;
        for (int c = shopsCol + 1; c < headerRow.Count; c++)
        {
            if (headerRow[c]?.Trim().Equals("#shops", StringComparison.OrdinalIgnoreCase) == true)
            {
                secondShops = c;
                break;
            }
        }
        if (secondShops > 0) blockWidth = secondShops - shopsCol;

        for (int r = 1; r < grid.Count; r++)
        {
            var label = GetCell(grid, r, typeCol);
            if (!label.Equals("total", StringComparison.OrdinalIgnoreCase)) continue;
            for (int offset = 0; offset + totalCol < headerRow.Count && offset < 1000 * blockWidth; offset += blockWidth)
            {
                int total = GetInt(grid, r, totalCol + offset);
                if (total == 0 && GetString(grid, r, totalCol + offset) == string.Empty) continue;
                report.DailyGlobal.Add(new DailyGlobalRow
                {
                    Pod              = "ALL",
                    Queued           = GetInt(grid, r, queueCol + offset),
                    Handled          = GetInt(grid, r, handledCol + offset),
                    MissedCalls      = GetInt(grid, r, missedCol + offset),
                    TransferredCalls = GetInt(grid, r, transCol + offset),
                });
            }
            break; // only the total row
        }
        return report.DailyGlobal.Count > 0;
    }

    /// <summary>
    /// <c>overflow_calls_-_last_1_complete_day_(15_min_interval).csv</c>:
    /// time series with one row per 15-min interval. Two columns: Queue, Overflow.
    /// Exposed as a new <c>ShopCallMetrics</c> row per interval under a virtual
    /// shop "OVERFLOW_15MIN". The frontend can ignore this for now; it lives in
    /// the export only.
    /// </summary>
    private static bool TryParseOverflow15Min(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int minuteCol = FindColumn(headerRow, "Call initiation Timestamp Minute15");
        int queueCol  = FindColumn(headerRow, "Queue Calls");
        int overflowCol = FindColumn(headerRow, "Overflow Calls");
        if (minuteCol < 0 || queueCol < 0) return false;

        int count = 0;
        for (int r = 1; r < grid.Count; r++)
        {
            var ts = GetCell(grid, r, minuteCol);
            if (string.IsNullOrWhiteSpace(ts)) continue;
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                continue;
            report.ShopCallMetrics.Add(new ShopCallMetricsRow
            {
                WeekStart     = dt.Date,
                ShopId        = "OVERFLOW_15MIN",
                ShopName      = "OVERFLOW_15MIN",
                PodId         = "ALL",
                QueueCalls    = GetInt(grid, r, queueCol),
                OverflowCalls = overflowCol >= 0 ? GetInt(grid, r, overflowCol) : 0,
            });
            count++;
        }
        return count > 0;
    }

    /// <summary>
    /// <c>overflow_calls_-_last_7_complete_days_(hourly_interval).csv</c>:
    /// time series with one row per hour. Two columns: Queue, Overflow.
    /// </summary>
    private static bool TryParseOverflowHourly(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int hourCol = FindColumn(headerRow, "Call initiation Timestamp Hour");
        int queueCol  = FindColumn(headerRow, "Queue Calls");
        int overflowCol = FindColumn(headerRow, "Overflow Calls");
        if (hourCol < 0 || queueCol < 0) return false;

        int count = 0;
        for (int r = 1; r < grid.Count; r++)
        {
            var ts = GetCell(grid, r, hourCol);
            if (string.IsNullOrWhiteSpace(ts)) continue;
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                continue;
            report.ShopCallMetrics.Add(new ShopCallMetricsRow
            {
                WeekStart     = dt.Date,
                ShopId        = "OVERFLOW_HOURLY",
                ShopName      = "OVERFLOW_HOURLY",
                PodId         = "ALL",
                QueueCalls    = GetInt(grid, r, queueCol),
                OverflowCalls = overflowCol >= 0 ? GetInt(grid, r, overflowCol) : 0,
            });
            count++;
        }
        return count > 0;
    }

    /// <summary>
    /// <c>agent_level_metrics.csv</c> (WEEKLY version, no <c>_daily</c> suffix):
    /// pivoted by week with 11 metric columns per week. Header names differ
    /// from the daily version: <c>Avg Speed to Answer</c> (ASA), <c>Avg Agent
    /// Interaction Duration Seconds</c> (AHT), <c>Avg Agent After Contact Work
    /// Duration Seconds</c> (ACW), <c>%SL under 15sec</c>, <c>%transferred of
    /// handled</c>. Maps to <c>DailyAgents</c> (one row per agent, weekly sums).
    /// </summary>
    private static bool TryParseAgentLevelWeeklyPivoted(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var headerRow = grid[1];
        int agentCol   = FindColumn(headerRow, "Agent Username");
        int routingCol = FindColumn(headerRow, "Agent Routing Profile Name");
        if (agentCol < 0) return false;

        var dateRow = grid[0];
        var dayStarts = new List<int>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            if (DateTime.TryParse(dateRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                dayStarts.Add(c);
            }
        }
        if (dayStarts.Count == 0) return false;

        // Weekly block: Handled, Transferred, Holds, AvgHold, AvgSpeed (ASA),
        // AvgInteraction (AHT), AvgAfterContact (ACW), %OnHold, %SL<15, %Transfers = 10
        const int blockWidth = 10;

        for (int r = 2; r < grid.Count; r++)
        {
            var agent = GetCell(grid, r, agentCol);
            if (string.IsNullOrWhiteSpace(agent) || !agent.Contains('@')) continue;

            var routing = routingCol >= 0 ? GetCell(grid, r, routingCol) : string.Empty;
            var (pod, _) = SplitRoutingProfile(routing);

            int hc = 0, tc = 0, holds = 0;
            double avgHold = 0, asa = 0, aht = 0, acw = 0, pctHold = 0, pctSL = 0, pctTrans = 0;
            int blocks = 0;
            for (int c = 0; c < dayStarts.Count; c++)
            {
                int m0 = dayStarts[c] + 1;
                int hcDay = GetInt(grid, r, m0);
                if (hcDay == 0 && GetString(grid, r, m0) == string.Empty) continue;
                hc        += hcDay;
                tc        += GetInt(grid, r, m0 + 1);
                holds     += GetInt(grid, r, m0 + 2);
                avgHold   += GetDouble(grid, r, m0 + 3);
                asa       += GetDouble(grid, r, m0 + 4);
                aht       += GetDouble(grid, r, m0 + 5);
                acw       += GetDouble(grid, r, m0 + 6);
                pctHold   += GetDouble(grid, r, m0 + 7);
                pctSL     += GetDouble(grid, r, m0 + 8);
                pctTrans  += GetDouble(grid, r, m0 + 9);
                blocks++;
                if (blockWidth <= 0) break;
            }
            if (blocks == 0) continue;

            report.DailyAgents.Add(new DailyAgentRow
            {
                Pod                = pod,
                AgentEmail         = agent,
                HC                 = hc,
                TC                 = tc,
                NumberOfHolds      = holds,
                AvgHoldTime        = SafeAvg(avgHold, blocks),
                ASA                = SafeAvg(asa, blocks),
                AHT                = SafeAvg(aht, blocks),
                ACW                = SafeAvg(acw, blocks),
                PctContactsOnHold  = SafeAvg(pctHold, blocks),
                PctSLUnder15Sec    = SafeAvg(pctSL, blocks),
                PctTransfers       = SafeAvg(pctTrans, blocks),
            });
        }
        return report.DailyAgents.Count > 0;
    }

    /// <summary>
    /// <c>external_overflow_calls_last_2_weeks_(by_day).csv</c>:
    /// non-pivoted time series with one row per day. Three columns:
    /// <c>Call initiation Timestamp Date</c>, <c>Queue Calls</c>,
    /// <c>Overflow calls</c>. Maps to <c>ShopCallMetrics</c> as a virtual
    /// shop so the user can see the overflow trend alongside the rest.
    /// </summary>
    private static bool TryParseExternalOverflowDaily(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int dateCol     = FindColumn(headerRow, "Call initiation Timestamp Date");
        int queueCol    = FindColumn(headerRow, "Queue Calls");
        int overflowCol = FindColumn(headerRow, "Overflow calls") >= 0
            ? FindColumn(headerRow, "Overflow calls")
            : FindColumn(headerRow, "Overflow Calls");
        if (dateCol < 0 || queueCol < 0) return false;

        int count = 0;
        for (int r = 1; r < grid.Count; r++)
        {
            var ts = GetCell(grid, r, dateCol);
            if (string.IsNullOrWhiteSpace(ts)) continue;
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                continue;
            report.ShopCallMetrics.Add(new ShopCallMetricsRow
            {
                WeekStart     = dt.Date,
                ShopId        = "EXTERNAL_OVERFLOW_DAILY",
                ShopName      = "EXTERNAL_OVERFLOW_DAILY",
                PodId         = "ALL",
                QueueCalls    = GetInt(grid, r, queueCol),
                OverflowCalls = overflowCol >= 0 ? GetInt(grid, r, overflowCol) : 0,
            });
            count++;
        }
        return count > 0;
    }

    /// <summary>
    /// <c>pods_generating_overflow_(spanish|english_speaking).csv</c>:
    /// pivoted by week. Per (Queue, Pod) row: <c>#overflow calls</c> and
    /// <c>%overflow</c> for each week. We surface it in <c>DailyGlobal</c> as
    /// "overflow generated" for each pod, so the user sees it in the same
    /// chart as the regular call volume.
    /// </summary>
    private static bool TryParsePodsGeneratingOverflow(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var headerRow = grid[1];
        int queueCol = FindColumn(headerRow, "Queue Name");
        int podCol   = FindColumn(headerRow, "Pod ID");
        int callsCol = FindColumn(headerRow, "#overflow calls");
        if (queueCol < 0 || podCol < 0 || callsCol < 0) return false;

        var dateRow = grid[0];
        var dayStarts = new List<int>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            if (DateTime.TryParse(dateRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                dayStarts.Add(c);
            }
        }
        if (dayStarts.Count == 0) return false;

        const int blockWidth = 2; // #overflow calls, %overflow

        for (int r = 2; r < grid.Count; r++)
        {
            var pod = GetCell(grid, r, podCol);
            if (string.IsNullOrWhiteSpace(pod)) continue;
            int total = 0;
            int blocks = 0;
            for (int c = 0; c < dayStarts.Count; c++)
            {
                int m0 = dayStarts[c] + 1;
                int calls = GetInt(grid, r, m0);
                if (calls == 0) continue;
                total += calls;
                blocks++;
                if (blockWidth <= 0) break;
            }
            if (total == 0) continue;

            report.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod              = pod,
                Queued           = 0,
                Handled          = 0,
                MissedCalls      = 0,
                TransferredCalls = total,
            });
        }
        return report.DailyGlobal.Count > 0;
    }

    /// <summary>
    /// <c>pods_helping_with_overflow_(spanish|english_speaking).csv</c>:
    /// the spanish variant has data pivoted by week (Queue Name, Pod, #handled,
    /// %handled); the english variant is header-only. Layout:
    ///   row 0: <c>,,week,2026-05-11,...</c> (dates)
    ///   row 1: <c>,Queue Name,POD helping with the overflow,#handled overflow
    ///          calls,% handled overflow,...</c> (column labels)
    /// </summary>
    private static bool TryParsePodsHelpingOverflow(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid.Count > 1 ? grid[1] : grid[0];
        bool hasHandled = headerRow.Any(c =>
            c?.Trim().Replace(" ", string.Empty)
                 .Equals("%handledoverflow", StringComparison.OrdinalIgnoreCase) == true
            || c?.Trim().Equals("#handled overflow calls", StringComparison.OrdinalIgnoreCase) == true);
        bool hasPodHelping = headerRow.Any(c => c?.Trim().StartsWith("POD helping", StringComparison.OrdinalIgnoreCase) == true);
        if (!hasHandled && !hasPodHelping) return false;

        if (grid.Count < 3)
        {
            // The english variant is header-only; nothing to extract.
            return false;
        }

        int queueCol = FindColumn(headerRow, "Queue Name");
        int podCol   = FindColumn(headerRow, "POD helping with the overflow");
        if (queueCol < 0 && podCol < 0) return false;

        var dateRow = grid[0];
        var dayStarts = new List<int>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            if (DateTime.TryParse(dateRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                dayStarts.Add(c);
            }
        }
        if (dayStarts.Count == 0) return false;

        // Each weekly block = 2 columns: #handled, %handled.
        const int blockWidth = 2;

        for (int r = 2; r < grid.Count; r++)
        {
            var pod = podCol >= 0 ? GetCell(grid, r, podCol) : string.Empty;
            if (string.IsNullOrWhiteSpace(pod)) continue;
            int total = 0;
            for (int c = 0; c < dayStarts.Count; c++)
            {
                int m0 = dayStarts[c] + 1;
                total += GetInt(grid, r, m0);
            }
            if (total == 0) continue;
            report.DailyGlobal.Add(new DailyGlobalRow
            {
                Pod              = pod,
                Queued           = 0,
                Handled          = total,
                MissedCalls      = 0,
                TransferredCalls = 0,
            });
        }
        return report.DailyGlobal.Count > 0;
    }

    /// <summary>
    /// <c>open_food_items_by_product_name.csv</c>: pivoted by week. One row per
    /// product. Two columns per week: <c>Orders with open_food_items</c> and
    /// <c>Open food items count</c>. We surface it as one <c>ShopDaily</c> row
    /// per product (prefixed <c>OPEN_FOOD:&lt;name&gt;</c>) so the user can
    /// see the issue hotspots in the same table.
    /// </summary>
    private static bool TryParseOpenFoodItemsByProduct(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;
        var headerRow = grid[1];
        int productCol = FindColumn(headerRow, "Product Name");
        int ordersCol  = FindColumn(headerRow, "Orders with open_food_items");
        int itemsCol   = FindColumn(headerRow, "Open food items count");
        if (productCol < 0 || ordersCol < 0 || itemsCol < 0) return false;

        var dateRow = grid[0];
        var dayStarts = new List<int>();
        for (int c = 0; c < dateRow.Count; c++)
        {
            if (DateTime.TryParse(dateRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                dayStarts.Add(c);
            }
        }
        if (dayStarts.Count == 0) return false;

        const int blockWidth = 2; // orders, items

        for (int r = 2; r < grid.Count; r++)
        {
            var product = GetCell(grid, r, productCol);
            if (string.IsNullOrWhiteSpace(product)) continue;
            int totalOrders = 0, totalItems = 0;
            for (int c = 0; c < dayStarts.Count; c++)
            {
                int m0 = dayStarts[c] + 1;
                totalOrders += GetInt(grid, r, m0);
                totalItems  += GetInt(grid, r, m0 + 1);
                if (blockWidth <= 0) break;
            }
            if (totalOrders == 0 && totalItems == 0) continue;
            report.ShopDaily.Add(new ShopDailyRow
            {
                ShopName       = $"OPEN_FOOD:{product}",
                TotalOrders    = totalOrders,
                RefundedOrders = 0,
                ErrorRate      = 0,
                ConversionRate = 0,
            });
        }
        return report.ShopDaily.Count > 0;
    }

    /// <summary>
    /// <c>shop_level_-_items_remove_by_order_shipping_type_.csv</c>:
    /// pivoted by week × shipping type. Layout is three-header-rows deep:
    ///   row 0: <c>,,Created At Week,2026-04-20,...</c> (dates)
    ///   row 1: <c>,,Shipping Type,delivery,for_here,pickup,to_go,...</c> (sub-headers)
    ///   row 2: <c>,Shop ID,Name,Removed Items Count,Removed Items Count,...</c> (column labels)
    /// We collapse it into one <c>ShopDaily</c> row per (Shop, ShippingType) pair,
    /// prefixed <c>REMOVED:&lt;id&gt;:&lt;shipping&gt;</c>.
    /// </summary>
    private static bool TryParseItemsRemovedByShippingType(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 4) return false;

        // row 0 = dates
        // row 1 = shipping types
        // row 2 = column labels
        var datesRow   = grid[0];
        var shippingRow = grid[1];
        var headerRow   = grid[2];

        int shopIdCol = FindColumn(headerRow, "Shop ID");
        if (shopIdCol < 0) return false;

        // Walk every column; pick up dates from row 0, shipping type from row 1,
        // and identify which "Removed Items Count" columns belong to which week.
        var weekStarts = new List<int>();
        var shippingTypes = new List<string>();
        var removedCols = new List<int>();
        for (int c = 0; c < datesRow.Count && c < headerRow.Count; c++)
        {
            if (DateTime.TryParse(datesRow[c]?.Trim() ?? string.Empty,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
            {
                weekStarts.Add(c);
                if (c < shippingRow.Count)
                {
                    var st = shippingRow[c]?.Trim();
                    shippingTypes.Add(string.IsNullOrEmpty(st) ? "unknown" : st);
                }
                else
                {
                    shippingTypes.Add("unknown");
                }
                if (headerRow[c]?.Trim().Equals("Removed Items Count", StringComparison.OrdinalIgnoreCase) == true)
                {
                    removedCols.Add(c);
                }
            }
        }
        if (weekStarts.Count == 0) return false;

        for (int r = 3; r < grid.Count; r++)
        {
            var shopId = GetCell(grid, r, shopIdCol);
            if (string.IsNullOrWhiteSpace(shopId)) continue;

            for (int w = 0; w < weekStarts.Count; w++)
            {
                int dateCol = weekStarts[w];
                // Find the Removed Items Count column for this week: it's the
                // first "Removed Items Count" header at or after dateCol.
                int removedCol = -1;
                foreach (var rc in removedCols)
                {
                    if (rc >= dateCol) { removedCol = rc; break; }
                }
                if (removedCol < 0) continue;
                int removed = GetInt(grid, r, removedCol);
                if (removed == 0) continue;
                var shipping = w < shippingTypes.Count ? shippingTypes[w] : "unknown";
                report.ShopDaily.Add(new ShopDailyRow
                {
                    ShopName       = $"REMOVED:{shopId}:{shipping}",
                    TotalOrders    = 0,
                    RefundedOrders = removed,
                    ErrorRate      = 0,
                    ConversionRate = 0,
                });
            }
        }
        return report.ShopDaily.Count > 0;
    }

    /// <summary>
    /// <c>shop_level_-_orders_metrics.csv</c> (WEEKLY, 6 columns per week):
    /// Orders, Open food items, Refunded, Orders with items removed, Removed
    /// Items Count, %orders with errors. The CSV has three header rows: dates
    /// in row 0, blank row 1, column labels in row 2. One row per shop.
    /// We map every week into a single aggregated <c>ShopDaily</c> row with
    /// prefixed <c>WEEKLY_ORDERS:&lt;shopId&gt;</c>.
    /// </summary>
    private static bool TryParseShopLevelOrdersWeekly(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 3) return false;

        // row 0 = dates, row 1 = sub-header (often blank), row 2 = column labels
        var datesRow = grid[0];
        var headerRow = grid.Count > 2 ? grid[2] : grid[1];

        int shopIdCol = FindColumn(headerRow, "Shop ID");
        int ordersCol = FindColumn(headerRow, "Orders Count");
        int refundCol = FindColumn(headerRow, "Refunded Orders");
        int errorsCol = FindColumn(headerRow, "%orders with errors");
        if (shopIdCol < 0 || ordersCol < 0) return false;

        const int blockWidth = 6; // Orders, OpenFoodItems, Refunded, ItemsRemoved, RemovedCount, %errors

        for (int r = 3; r < grid.Count; r++)
        {
            var shopId = GetCell(grid, r, shopIdCol);
            if (string.IsNullOrWhiteSpace(shopId)) continue;

            int total = 0, refunded = 0;
            double pctErrors = 0;
            int blocks = 0;
            // Find the first date column in row 0; metrics start at the same col
            // and the block repeats every blockWidth cols.
            int firstDateCol = -1;
            for (int c = 0; c < datesRow.Count; c++)
            {
                if (DateTime.TryParse(datesRow[c]?.Trim() ?? string.Empty,
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _))
                {
                    firstDateCol = c;
                    break;
                }
            }
            if (firstDateCol < 0) continue;

            for (int offset = 0;
                 offset + firstDateCol + blockWidth <= headerRow.Count && offset < 1000 * blockWidth;
                 offset += blockWidth)
            {
                int baseCol = firstDateCol + offset;
                int o  = GetInt(grid, r, baseCol);
                int r2 = refundCol >= 0 ? GetInt(grid, r, refundCol + offset) : 0;
                double e = errorsCol >= 0 ? GetDouble(grid, r, errorsCol + offset) : 0;
                if (o == 0 && r2 == 0) continue;
                total    += o;
                refunded += r2;
                pctErrors += e;
                blocks++;
            }
            if (blocks == 0) continue;

            report.ShopDaily.Add(new ShopDailyRow
            {
                ShopName       = $"WEEKLY_ORDERS:{shopId}",
                ShopId         = shopId,
                TotalOrders    = total,
                RefundedOrders = refunded,
                ErrorRate      = SafeAvg(pctErrors, blocks),
                ConversionRate = 0,
            });
        }
        return report.ShopDaily.Count > 0;
    }

    /// <summary>
    /// <c>_orders_from_calls.csv</c> (note the leading underscore — differs
    /// from the original <c>_order_from_calls.csv</c> in the first ZIP). Same
    /// layout: <c>Date | Total Calls | Distinct Customer Numbers | Order Count
    /// | %orders from calls | %orders from unique customers</c>.
    /// </summary>
    private static bool TryParseOrdersFromCalls(IList<IList<string>> grid, SliceReport report)
    {
        if (grid.Count < 2) return false;
        var headerRow = grid[0];
        int orderCountCol = FindColumn(headerRow, "Order Count");
        int totalCallsCol  = FindColumn(headerRow, "Total Calls");
        if (orderCountCol < 0 || totalCallsCol < 0) return false;

        int totalOrders = 0, totalCalls = 0, rows = 0;
        for (int r = 1; r < grid.Count; r++)
        {
            int orderCount = GetInt(grid, r, orderCountCol);
            int rowTotal   = GetInt(grid, r, totalCallsCol);
            if (orderCount == 0 && rowTotal == 0) continue;
            totalOrders += orderCount;
            totalCalls  += rowTotal;
            rows++;
        }
        if (rows == 0) return false;

        report.ShopDaily.Add(new ShopDailyRow
        {
            ShopName       = "ORDERS_FROM_CALLS",
            TotalOrders    = totalOrders,
            RefundedOrders = 0,
            ErrorRate      = 0,
            ConversionRate = totalCalls == 0 ? 0 : Math.Round((double)totalOrders / totalCalls * 100.0, 1),
        });
        return true;
    }
}
