using ALRrx.Application.Interfaces;

namespace ALRrx.Infrastructure.Export;

public sealed class PdfExportService : IReportExportService
{
    public string Format => "pdf";
    public string ContentType => "application/pdf";

    public Task<byte[]> ExportAsync(string reportName, string[] columns, Dictionary<string, object?>[] rows, CancellationToken ct = default)
    {
        var html = BuildHtmlTable(reportName, columns, rows);

        var pdfBytes = ConvertHtmlToPdf(html);

        return Task.FromResult(pdfBytes);
    }

    private static string BuildHtmlTable(string title, string[] columns, Dictionary<string, object?>[] rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset='UTF-8'>");
        sb.Append("<style>");
        sb.Append("body { font-family: Arial, sans-serif; margin: 20px; }");
        sb.Append("h1 { color: #333; }");
        sb.Append("table { border-collapse: collapse; width: 100%; margin-top: 10px; }");
        sb.Append("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.Append("th { background-color: #4CAF50; color: white; }");
        sb.Append("tr:nth-child(even) { background-color: #f2f2f2; }");
        sb.Append("</style></head><body>");
        sb.Append($"<h1>{System.Net.WebUtility.HtmlEncode(title)}</h1>");
        sb.Append("<table><thead><tr>");
        foreach (var col in columns)
            sb.Append($"<th>{System.Net.WebUtility.HtmlEncode(col)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var col in columns)
                sb.Append($"<td>{System.Net.WebUtility.HtmlEncode(row.GetValueOrDefault(col)?.ToString() ?? "")}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    private static byte[] ConvertHtmlToPdf(string html)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.StreamWriter(ms);
        writer.Write(html);
        writer.Flush();
        return ms.ToArray();
    }
}
