namespace ALRrx.Application.Interfaces;

public interface IReportExportService
{
    string Format { get; }
    string ContentType { get; }
    Task<byte[]> ExportAsync(string reportName, string[] columns, Dictionary<string, object?>[] rows, CancellationToken ct = default);
}
