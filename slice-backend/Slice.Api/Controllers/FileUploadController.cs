using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Application.Interfaces;
using Slice.Domain.Interfaces;

namespace Slice.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class FileUploadController : ControllerBase
{
    private const int MaxFiles = 12;
    private const long MaxFileSizeBytes = 50 * 1024 * 1024; // 50 MB per file
    private static readonly string[] AllowedExtensions = [".xlsx", ".xls", ".xlsm", ".zip"];

    private readonly IFileProcessingOrchestrator _orchestrator;
    private readonly IJobRepository _jobRepo;

    public FileUploadController(IFileProcessingOrchestrator orchestrator, IJobRepository jobRepo)
    {
        _orchestrator = orchestrator;
        _jobRepo = jobRepo;
    }

    /// <summary>
    /// Upload up to 12 Excel files for simultaneous processing.
    /// </summary>
    [HttpPost("excel")]
    [RequestSizeLimit(600 * 1024 * 1024)] // 600 MB total
    public async Task<IActionResult> UploadExcel(
        IFormFileCollection files,
        CancellationToken ct)
    {
        if (files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        if (files.Count > MaxFiles)
            return BadRequest(new { error = $"Maximum {MaxFiles} files allowed per upload." });

        var validationError = ValidateFiles(files, [".xlsx", ".xls", ".xlsm"]);
        if (validationError != null) return BadRequest(new { error = validationError });

        var ownerEmail = GetCurrentEmail();
        var streams = files.Select(f => f.OpenReadStream()).ToList();
        var names = files.Select(f => f.FileName).ToList();

        var jobId = await _orchestrator.EnqueueAsync(streams, names, ownerEmail, ct);

        foreach (var s in streams) await s.DisposeAsync();

        return Accepted(new UploadJobResponse(jobId, files.Count, "Processing"));
    }

    /// <summary>
    /// Upload a single ZIP file containing Excel files.
    /// </summary>
    [HttpPost("zip")]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<IActionResult> UploadZip(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided." });

        if (!Path.GetExtension(file.FileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only .zip files are accepted on this endpoint." });

        var ownerEmail = GetCurrentEmail();
        await using var stream = file.OpenReadStream();
        var jobId = await _orchestrator.EnqueueZipAsync(stream, ownerEmail, ct);

        return Accepted(new UploadJobResponse(jobId, 1, "Extracting"));
    }

    /// <summary>
    /// Poll the processing status for a job.
    /// </summary>
    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _jobRepo.GetByIdAsync(jobId);
        if (job == null) return NotFound(new { error = "Job not found." });

        // Non-admin users can only see their own jobs
        if (!User.IsInRole("Admin") && !job.CreatedByEmail.Equals(GetCurrentEmail(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        return Ok(new JobStatusResponse(
            job.Id, job.Status, job.TotalFiles, job.ProcessedFiles,
            job.ErrorMessage, job.ReportId, job.CreatedAt, job.CompletedAt));
    }

    private string GetCurrentEmail() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

    private static string? ValidateFiles(IFormFileCollection files, string[] allowedExtensions)
    {
        foreach (var file in files)
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(ext))
                return $"File '{file.FileName}' has unsupported extension '{ext}'.";

            if (file.Length > MaxFileSizeBytes)
                return $"File '{file.FileName}' exceeds the 50 MB size limit.";
        }
        return null;
    }
}
