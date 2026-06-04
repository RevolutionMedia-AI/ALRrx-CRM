using Slice.Domain.Entities;

namespace Slice.Application.Interfaces;

public interface IExcelParserService
{
    /// <summary>
    /// Parses a single Excel file into its Slice report sections.
    /// Returns null if the file doesn't match the expected template.
    /// </summary>
    Task<SliceReport?> ParseAsync(string filePath, CancellationToken ct = default);
}
