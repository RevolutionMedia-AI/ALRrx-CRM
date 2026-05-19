using ALRrx.Application.Interfaces;
using OfficeOpenXml;

namespace ALRrx.Infrastructure.Export;

public sealed class ExcelExportService : IReportExportService
{
    public string Format => "excel";
    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    static ExcelExportService()
    {
        ExcelPackage.License.SetNonCommercialOrganization("ALRrx CRM");
    }

    public Task<byte[]> ExportAsync(string reportName, string[] columns, Dictionary<string, object?>[] rows, CancellationToken ct = default)
    {
        using var package = new ExcelPackage();
        var worksheet = package.Workbook.Worksheets.Add(reportName[..Math.Min(reportName.Length, 31)]);

        for (var col = 0; col < columns.Length; col++)
            worksheet.Cells[1, col + 1].Value = columns[col];

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < columns.Length; col++)
            {
                worksheet.Cells[row + 2, col + 1].Value = rows[row].GetValueOrDefault(columns[col]) ?? string.Empty;
            }
        }

        worksheet.Cells[worksheet.Dimension!.Address].AutoFitColumns();

        return Task.FromResult(package.GetAsByteArray());
    }
}
