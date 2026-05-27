using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Slice.Application.Interfaces;
using Slice.Domain.Entities;
using Slice.Domain.Enums;
using Slice.Domain.Interfaces;

namespace Slice.Infrastructure.Processing;

public sealed class FileProcessingOrchestrator : IFileProcessingOrchestrator
{
    // Maximum simultaneous Excel files per batch
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

        // Save temp files before releasing the request streams
        var tempPaths = new List<string>(fileStreams.Count);
        for (int i = 0; i < fileStreams.Count; i++)
        {
            var ext = Path.GetExtension(fileNames[i]);
            var tempPath = Path.Combine(Path.GetTempPath(), "slice", job.Id.ToString(), $"file_{i}{ext}");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

            await using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await fileStreams[i].CopyToAsync(fs, ct);
            tempPaths.Add(tempPath);
        }

        job.SourceFiles = tempPaths;
        await _jobRepo.SaveAsync(job);

        // Fire-and-forget background processing
        _ = ProcessFilesInternalAsync(job, tempPaths, CancellationToken.None);

        return job.Id;
    }

    public async Task<Guid> EnqueueZipAsync(Stream zipStream, string ownerEmail, CancellationToken ct = default)
    {
        var job = new ProcessingJob { CreatedByEmail = ownerEmail };
        await _jobRepo.SaveAsync(job);

        // Save ZIP to temp first to free the HTTP request stream
        var tempZip = Path.Combine(Path.GetTempPath(), "slice", job.Id.ToString(), "upload.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(tempZip)!);

        await using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await zipStream.CopyToAsync(fs, ct);

        _ = ProcessZipInternalAsync(job, tempZip, CancellationToken.None);

        return job.Id;
    }

    private async Task ProcessZipInternalAsync(ProcessingJob job, string zipPath, CancellationToken ct)
    {
        try
        {
            job.Status = JobStatus.Extracting;
            await _jobRepo.UpdateAsync(job);

            await using var zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            var extractedFiles = await _zipExtractor.ExtractAsync(zipStream, job.Id.ToString(), ct);

            if (extractedFiles.Count == 0)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "No Excel files found in ZIP.";
                await _jobRepo.UpdateAsync(job);
                return;
            }

            if (extractedFiles.Count > MaxConcurrentFiles)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = $"ZIP contains {extractedFiles.Count} files; maximum is {MaxConcurrentFiles}.";
                await _jobRepo.UpdateAsync(job);
                return;
            }

            job.TotalFiles = extractedFiles.Count;
            job.SourceFiles = extractedFiles.ToList();
            await _jobRepo.UpdateAsync(job);

            await ProcessFilesInternalAsync(job, extractedFiles, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ZIP processing failed for job {JobId}", job.Id);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            await _jobRepo.UpdateAsync(job);
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

        // Parse files using a channel for bounded parallelism
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MaxConcurrentFiles)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });

        var parsedReports = new System.Collections.Concurrent.ConcurrentBag<SliceReport>();
        var semaphore = new SemaphoreSlim(MaxConcurrentFiles, MaxConcurrentFiles);

        var producerTask = Task.Run(async () =>
        {
            foreach (var path in filePaths)
                await channel.Writer.WriteAsync(path, ct);
            channel.Writer.Complete();
        }, ct);

        var consumerTasks = Enumerable.Range(0, Math.Min(filePaths.Count, MaxConcurrentFiles))
            .Select(_ => Task.Run(async () =>
            {
                await foreach (var path in channel.Reader.ReadAllAsync(ct))
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var report = await _excelParser.ParseAsync(path, ct);
                        if (report != null) parsedReports.Add(report);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse {File}", path);
                    }
                    finally
                    {
                        job.ProcessedFiles++;
                        await _jobRepo.UpdateAsync(job);
                        semaphore.Release();
                        TryDeleteFile(path);
                    }
                }
            }, ct))
            .ToList();

        await Task.WhenAll([producerTask, .. consumerTasks]);

        if (parsedReports.IsEmpty)
        {
            job.Status = JobStatus.Failed;
            job.ErrorMessage = "No valid Slice report data found in any uploaded file.";
            await _jobRepo.UpdateAsync(job);
            return;
        }

        job.Status = JobStatus.Merging;
        await _jobRepo.UpdateAsync(job);

        var merged = _mergeService.Merge(parsedReports);
        merged.GeneratedByEmail = job.CreatedByEmail;

        var xlsxPath = await _mergeService.ExportXlsxAsync(merged, ct);
        var csvPath = await _mergeService.ExportCsvAsync(merged, ct);
        merged.MergedXlsxPath = xlsxPath;
        merged.MergedCsvPath = csvPath;

        await _reportRepo.SaveAsync(merged);

        job.Status = JobStatus.Completed;
        job.CompletedAt = DateTime.UtcNow;
        job.ReportId = merged.Id;
        await _jobRepo.UpdateAsync(job);

        _logger.LogInformation("Job {JobId} completed. Report {ReportId} created.", job.Id, merged.Id);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
