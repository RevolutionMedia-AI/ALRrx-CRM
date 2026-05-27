using Slice.Domain.Entities;

namespace Slice.Application.Interfaces;

public interface IReportMergeService
{
    /// <summary>
    /// Merges multiple SliceReports into a single combined report.
    /// </summary>
    SliceReport Merge(IEnumerable<SliceReport> reports);

    /// <summary>
    /// Exports the merged report as an XLSX file. Returns the file path.
    /// </summary>
    Task<string> ExportXlsxAsync(SliceReport report, CancellationToken ct = default);

    /// <summary>
    /// Exports the merged report as a CSV file. Returns the file path.
    /// </summary>
    Task<string> ExportCsvAsync(SliceReport report, CancellationToken ct = default);
}
