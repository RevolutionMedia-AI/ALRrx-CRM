using OfficeOpenXml;
using OfficeOpenXml.Style;
using Slice.Domain.Entities;
using System.Drawing;

namespace Slice.Infrastructure.Excel;

/// <summary>
/// Builds a downloadable Excel template that mirrors the layout of
/// <c>Dashoard Draft (3).xlsx</c>: una sola hoja "Slice Report (Template)" con
/// 3 bloques apilados (Global + Agent + Shop). Sirve como punto de referencia
/// visual para que el usuario sepa qué estructura tiene el reporte exportado.
/// </summary>
public sealed class TemplateGeneratorService
{
    private static readonly Color HeaderColor = Color.FromArgb(31, 119, 180);

    private static readonly string[] DefaultPodPlaceholders = { "ES-12", "ES-16", "ES-17", "ES-18" };

    public string BuildTemplate(SliceReport report, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var filePath = Path.Combine(outDir, $"Slice_Template_{report.Id}.xlsx");

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Slice Report (Template)");

        int row = 1;

        // ── Bloque Global ────────────────────────────────────────────────────
        ws.Cells[row, 1].Value = "Global";
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        WriteHeader(ws, row, new[]
        {
            "Pod", "Queued", "Handle", "Missed Calls", "Transferred Calls",
            "%Queued", "%Handled", "%missed", "%Transferred", "Conv %",
            "Order Count", "Refunded  Orders", "% Orders with errors",
        });
        row++;

        // Filas placeholder con los PODs tipicos.
        foreach (var pod in DefaultPodPlaceholders)
        {
            ws.Cells[row, 1].Value = pod;
            row++;
        }

        row++; // separador

        // ── Bloque Agent ─────────────────────────────────────────────────────
        ws.Cells[row, 1].Value = "Agent";
        StyleSectionTitle(ws.Cells[row, 1, row, 13]);
        row++;

        var agentColumnHeaders = new[]
        {
            "Agent", "HC", "TC", "Number of Holds", "Avg. Hold Time", "ASA",
            "AHT", "ACW", "% Contacts on Hold", "%SL under 15 sec", "% Transfers", "Shift",
        };

        foreach (var pod in DefaultPodPlaceholders)
        {
            ws.Cells[row, 2].Value = "POD";
            ws.Cells[row, 3].Value = pod;
            ws.Cells[row, 10].Value = "Sup";
            ws.Cells[row, 11].Value = "Supervisor Name";
            StylePodHeaderRow(ws.Cells[row, 1, row, 13]);
            row++;

            // Headers en B-M (12 columnas) para que coincidan con los datos.
            WriteHeader(ws.Cells[row, 2, row, 13], agentColumnHeaders);
            row++;

            ws.Cells[row, 2].Value = "agent.name@slice.com";
            ws.Cells[row, 13].Value = "Full Time";
            row++;
            ws.Cells[row, 2].Value = "agent.name@slice.com";
            ws.Cells[row, 13].Value = "Part Time";
            row++;
        }

        row++; // separador

        // ── Bloque Shop ──────────────────────────────────────────────────────
        ws.Cells[row, 2].Value = "Shop";
        StyleSectionTitle(ws.Cells[row, 2, row, 18]);
        row++;

        // Headers en B-R (rango 2-18, 17 columnas) para que coincidan con la
        // posicion de los datos. Columna A queda vacia como margen.
        WriteHeader(ws.Cells[row, 2, row, 18], new[]
        {
            "Pod - Shops", "Shop ID", "Total Calls", "Overflow", "Queued", "Handle",
            "Missed Calls", "Transferred Calls", "%Overflow", "%Queued", "%Handled",
            "%missed", "%Transferred", "Order Count", "Conv %", "Refunded  Orders",
            "% Orders with errors",
        });
        row++;
        ws.Cells[row, 2].Value = "Capri Pizza Pasta Kabobs";
        ws.Cells[row, 3].Value = "73";
        row++;

        if (ws.Dimension != null) ws.Cells[ws.Dimension.Address].AutoFitColumns();
        package.SaveAs(new FileInfo(filePath));
        return filePath;
    }

    private static void WriteHeader(ExcelWorksheet ws, int row, string[] headers)
    {
        for (int col = 1; col <= headers.Length; col++)
        {
            ws.Cells[row, col].Value = headers[col - 1];
            ws.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
            ws.Cells[row, col].Style.Fill.BackgroundColor.SetColor(HeaderColor);
            ws.Cells[row, col].Style.Font.Color.SetColor(Color.White);
            ws.Cells[row, col].Style.Font.Bold = true;
        }
    }

    private static void WriteHeader(ExcelRange range, string[] headers)
    {
        int startRow = range.Start.Row;
        int startCol = range.Start.Column;
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = range.Worksheet.Cells[startRow, startCol + i];
            cell.Value = headers[i];
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(HeaderColor);
            cell.Style.Font.Color.SetColor(Color.White);
            cell.Style.Font.Bold = true;
        }
    }

    private static void StyleSectionTitle(ExcelRange range)
    {
        range.Merge = true;
        range.Style.Font.Bold = true;
        range.Style.Font.Size = 12;
        range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(240, 240, 240));
    }

    private static void StylePodHeaderRow(ExcelRange range)
    {
        range.Style.Fill.PatternType = ExcelFillStyle.Solid;
        range.Style.Fill.BackgroundColor.SetColor(HeaderColor);
        range.Style.Font.Color.SetColor(Color.White);
        range.Style.Font.Bold = true;
    }
}
