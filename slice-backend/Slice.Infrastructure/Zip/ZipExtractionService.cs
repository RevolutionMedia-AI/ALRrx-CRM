using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Slice.Application.Interfaces;

namespace Slice.Infrastructure.Zip;

/// <summary>
/// Extracts Excel files from a ZIP archive into a temp directory.
/// Uses a semaphore to cap concurrent disk writes and a <see cref="ConcurrentBag{T}"/>
/// for lock-free result collection.
/// </summary>
public sealed class ZipExtractionService : IZipExtractionService
{
    private static readonly HashSet<string> ExcelExtensions = [".xlsx", ".xls", ".xlsm", ".csv"];

    /// <summary>Maximum parallel extractions to avoid disk I/O contention.</summary>
    private const int MaxConcurrentExtractions = 4;

    private readonly ILogger<ZipExtractionService> _logger;

    public ZipExtractionService(ILogger<ZipExtractionService> logger) => _logger = logger;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ExtractAsync(Stream zipStream, string jobId, CancellationToken ct = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "slice", jobId);
        Directory.CreateDirectory(tempDir);

        var extractedFiles = new ConcurrentBag<string>();
        var semaphore      = new SemaphoreSlim(MaxConcurrentExtractions, MaxConcurrentExtractions);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);

        var tasks = archive.Entries
            .Where(e => ExcelExtensions.Contains(Path.GetExtension(e.Name).ToLowerInvariant()) && e.Length > 0)
            .Select(entry => ExtractEntryAsync(entry, tempDir, semaphore, extractedFiles, ct))
            .ToList();

        await Task.WhenAll(tasks);

        _logger.LogInformation("Extracted {Count} Excel files for job {JobId}", extractedFiles.Count, jobId);
        return extractedFiles.ToList();
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string targetDir,
        SemaphoreSlim semaphore,
        ConcurrentBag<string> results,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            // Sanitize the entry name to prevent path-traversal attacks.
            var safeName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(safeName)) return;

            var destPath = Path.Combine(targetDir, safeName);

            await using var entryStream = entry.Open();
            await using var fileStream  = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true);

            await entryStream.CopyToAsync(fileStream, ct);
            results.Add(destPath);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
