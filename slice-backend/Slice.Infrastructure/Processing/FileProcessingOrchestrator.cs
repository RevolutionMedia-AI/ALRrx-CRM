using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using Slice.Domain.Enums;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Processing;

/// <summary>
/// Coordinates the full pipeline: save to temp → parse Excel → merge → export.
/// Each uploaded batch runs as a fire-and-forget background task so the HTTP
/// request returns immediately with a <see cref="ProcessingJob"/> ID to poll.
/// </summary>
public sealed class FileProcessingOrchestrator : IFileProcessingOrchestrator
{
    /// <summary>Maximum Excel files processed in parallel per batch.</summary>
    private const int MaxConcurrentFiles = 12;

    private readonly IZipExtractionService _zipExtractor;
    private readonly IExcelParserService _excelParser;
    private readonly IReportMergeService _mergeService;
    private readonly IJobRepository _jobRepo;
    private readonly IReportRepository _reportRepo;
    private readonly ILogger<FileProcessingOrchestrator> _logger;

    public FileProcessingOrchestrator(
        IZipExtractionService zipExtractor,
        IExcelParserService excelParser,
        IReportMergeService mergeService,
        IJobRepository jobRepo,
        IReportRepository reportRepo,
        ILogger<FileProcessingOrchestrator> logger)
    {
        _zipExtractor = zipExtractor;
        _excelParser = excelParser;
        _mergeService = mergeService;
        _jobRepo = jobRepo;
        _reportRepo = reportRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> EnqueueAsync(
        IReadOnlyList<Stream> fileStreams,
        IReadOnlyList<string> fileNames,
        string ownerEmail,
        CancellationToken ct = default)
    {
        if (fileStreams.Count > MaxConcurrentFiles)
            throw new InvalidOperationException($"Maximum {MaxConcurrentFiles} files per upload.");

        var job = new ProcessingJob
        {
            CreatedByEmail = ownerEmail,
            TotalFiles = fileStreams.Count,
        };

        // Copy uploaded streams to temp files before releasing the HTTP request.
        var tempPaths = new List<string>(fileStreams.Count);
        for (int i = 0; i < fileStreams.Count; i++)
        {
            var ext      = Path.GetExtension(fileNames[i]);
            var tempPath = Path.Combine(Path.GetTempPath(), "slice", job.Id.ToString(), $"file_{i}{ext}");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, bufferSize: 81920, useAsync: true);
            await fileStreams[i].CopyToAsync(fs, ct);
            tempPaths.Add(tempPath);
        }

        job.SourceFiles = tempPaths;
        await _jobRepo.SaveAsync(job);

        // Fire-and-forget: processing continues after the HTTP response is returned.
        _ = ProcessFilesInternalAsync(job, tempPaths, CancellationToken.None);

        return job.Id;
    }

    /// <inheritdoc/>
    public async Task<Guid> EnqueueZipAsync(Stream zipStream, string ownerEmail, CancellationToken ct = default)
    {
        var job = new ProcessingJob { CreatedByEmail = ownerEmail };
        await _jobRepo.SaveAsync(job);

        // Persist the ZIP to a temp file so the HTTP request stream can be released.
        var tempZip = Path.Combine(Path.GetTempPath(), "slice", job.Id.ToString(), "upload.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(tempZip)!);

        await using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write,
                         FileShare.None, bufferSize: 81920, useAsync: true))
            await zipStream.CopyToAsync(fs, ct);

        _ = ProcessZipInternalAsync(job, tempZip, CancellationToken.None);

        return job.Id;
    }

    // ─── Internal pipeline ────────────────────────────────────────────────────

    private async Task ProcessZipInternalAsync(ProcessingJob job, string zipPath, CancellationToken ct)
    {
        try
        {
            job.Status = JobStatus.Extracting;
            await _jobRepo.UpdateAsync(job);

            await using var zipStream = new FileStream(zipPath, FileMode.Open,
                FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);
            var extractedFiles = await _zipExtractor.ExtractAsync(zipStream, job.Id.ToString(), ct);

            if (extractedFiles.Count == 0)
            {
                await FailJobAsync(job, "No Excel files found in ZIP.");
                return;
            }

            if (extractedFiles.Count > MaxConcurrentFiles)
            {
                await FailJobAsync(job, $"ZIP contains {extractedFiles.Count} files; maximum is {MaxConcurrentFiles}.");
                return;
            }

            job.TotalFiles  = extractedFiles.Count;
            job.SourceFiles = extractedFiles.ToList();
            await _jobRepo.UpdateAsync(job);

            await ProcessFilesInternalAsync(job, extractedFiles, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP processing failed for job {JobId}", job.Id);
            await FailJobAsync(job, ex.Message);
        }
        finally
        {
            TryDeleteFile(zipPath);
        }
    }

    private async Task ProcessFilesInternalAsync(ProcessingJob job, IReadOnlyList<string> filePaths, CancellationToken ct)
    {
        job.Status = JobStatus.Processing;
        await _jobRepo.UpdateAsync(job);

        var parsedReports = new ConcurrentBag<SliceReport>();

        // processedCount is incremented atomically so the job status bar stays accurate
        // even when multiple files finish near-simultaneously.
        var processedCount = 0;

        await Parallel.ForEachAsync(
            filePaths,
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentFiles, CancellationToken = ct },
            async (path, token) =>
            {
                try
                {
                    var report = await _excelParser.ParseAsync(path, token);
                    if (report != null) parsedReports.Add(report);
                    else _logger.LogWarning("No recognized layout in {File} (file skipped).", path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse {File}", path);
                }
                finally
                {
                    job.ProcessedFiles = Interlocked.Increment(ref processedCount);
                    await _jobRepo.UpdateAsync(job);
                    TryDeleteFile(path);
                }
            });

        if (parsedReports.IsEmpty)
        {
            var recognized = string.Join(", ", filePaths
                .Select(Path.GetFileName)
                .Where(n => n != null));
            await FailJobAsync(job, $"No valid Slice report data found in any uploaded file. Files seen: {recognized}.");
            return;
        }

        job.Status = JobStatus.Merging;
        await _jobRepo.UpdateAsync(job);

        var merged = _mergeService.Merge(parsedReports);
        merged.GeneratedByEmail = job.CreatedByEmail;

        merged.MergedXlsxPath = await _mergeService.ExportXlsxAsync(merged, ct);
        merged.MergedCsvPath  = await _mergeService.ExportCsvAsync(merged, ct);

        await _reportRepo.SaveAsync(merged);

        job.Status      = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.ReportId    = merged.Id;
        await _jobRepo.UpdateAsync(job);

        _logger.LogInformation("Job {JobId} completed. Report {ReportId} created.", job.Id, merged.Id);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task FailJobAsync(ProcessingJob job, string message)
    {
        job.Status       = JobStatus.Failed;
        job.ErrorMessage = message;
        await _jobRepo.UpdateAsync(job);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort cleanup */ }
    }
}
