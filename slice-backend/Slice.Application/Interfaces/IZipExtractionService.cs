namespace Slice.Application.Interfaces;

public interface IZipExtractionService
{
    /// <summary>
    /// Extracts Excel files from a ZIP stream into a temp directory.
    /// Returns the list of extracted .xlsx/.xls file paths.
    /// </summary>
    Task<IReadOnlyList<string>> ExtractAsync(Stream zipStream, string jobId, CancellationToken ct = default);
}
