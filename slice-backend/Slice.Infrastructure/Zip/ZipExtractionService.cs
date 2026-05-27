using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Slice.Application.Interfaces;

namespace Slice.Infrastructure.Zip;

public sealed class ZipExtractionService : IZipExtractionService
{
    private static readonly HashSet<string> ExcelExtensions = [".xlsx", ".xls", ".xlsm"];
    private readonly ILogger<ZipExtractionService> _logger;

    public ZipExtractionService(ILogger<ZipExtractionService> logger) => _logger = logger;

    public async Task<IReadOnlyList<string>> ExtractAsync(Stream zipStream, string jobId, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "slice", jobId);
        Directory.CreateDirectory(tempDir);

        var extractedFiles = new List<string>();

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        // Process entries in parallel (up to 4 concurrent extractions)
        var semaphore = new SemaphoreSlim(4, 4);
        var tasks = archive.Entries
            .Where(e => ExcelExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()) && e.Length > 0)
            .Select(entry => ExtractEntryAsync(entry, tempDir, semaphore, extractedFiles, ct))
            .ToList();

        await Task.WhenAll(tasks);

        _logger.LogInformation("Extracted {Count} Excel files for job {JobId}", extractedFiles.Count, jobId);
        return extractedFiles;
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string targetDir,
        SemaphoreSlim semaphore,
        List<string> resultList,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            // Sanitize entry name to prevent path traversal
            var safeName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(safeName)) return;

            var destPath = Path.Combine(targetDir, safeName);

            await using var entryStream = entry.Open();
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true);

            await entryStream.CopyToAsync(fileStream, ct);

            lock (resultList) { resultList.Add(destPath); }
        }
        finally
        {
            semaphore.Release();
        }
    }
}
