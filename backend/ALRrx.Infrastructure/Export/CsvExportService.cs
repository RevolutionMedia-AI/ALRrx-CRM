using System.Text;
using ALRrx.Application.Interfaces;

namespace ALRrx.Infrastructure.Export;

public sealed class CsvExportService : IReportExportService
{
    public string Format => "csv";
    public string ContentType => "text/csv";

    public Task<byte[]> ExportAsync(string reportName, string[] columns, Dictionary<string, object?>[] rows, CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(EscapeCsvField)));

        foreach (var row in rows)
        {
            var values = columns.Select(col => EscapeCsvField(row.GetValueOrDefault(col)?.ToString() ?? ""));
            sb.AppendLine(string.Join(",", values));
        }

        return Task.FromResult(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }
}
