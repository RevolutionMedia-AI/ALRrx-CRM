using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slice.Application.DTOs;
using Slice.Application.Interfaces;
using Slice.Domain.Interfaces;

namespace Slice.Api.Controllers;

/// <summary>
/// Gestiona la subida de archivos Excel y ZIP para su procesamiento asíncrono.
/// Todos los endpoints requieren autenticación JWT.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class FileUploadController : ControllerBase
{
    /// <summary>Máximo de archivos Excel por subida.</summary>
    private const int MaxFiles = 12;

    /// <summary>Tamaño máximo permitido por archivo Excel individual (50 MB).</summary>
    private const long MaxFileSizeBytes = 50 * 1024 * 1024;

    private readonly IFileProcessingOrchestrator _orchestrator;
    private readonly IJobRepository              _jobRepo;

    public FileUploadController(IFileProcessingOrchestrator orchestrator, IJobRepository jobRepo)
    {
        _orchestrator = orchestrator;
        _jobRepo      = jobRepo;
    }

    /// <summary>
    /// Sube hasta 12 archivos Excel (.xlsx / .xls / .xlsm) en una sola petición.
    /// El procesamiento ocurre en background; la respuesta incluye un <c>jobId</c>
    /// para hacer polling en <c>GET /api/fileupload/status/{jobId}</c>.
    /// Límite total de la petición: 600 MB.
    /// </summary>
    [HttpPost("excel")]
    [RequestSizeLimit(600 * 1024 * 1024)]
    public async Task<IActionResult> UploadExcel(IFormFileCollection files, CancellationToken ct)
    {
        if (files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        if (files.Count > MaxFiles)
            return BadRequest(new { error = $"Maximum {MaxFiles} files allowed per upload." });

        var validationError = ValidateFiles(files, [".xlsx", ".xls", ".xlsm", ".csv"]);
        if (validationError != null) return BadRequest(new { error = validationError });

        var ownerEmail = GetCurrentEmail();
        var streams    = files.Select(f => f.OpenReadStream()).ToList();
        var names      = files.Select(f => f.FileName).ToList();

        var jobId = await _orchestrator.EnqueueAsync(streams, names, ownerEmail, ct);

        foreach (var s in streams) await s.DisposeAsync();

        return Accepted(new UploadJobResponse(jobId, files.Count, "Processing"));
    }

    /// <summary>
    /// Sube un único archivo ZIP que contenga archivos Excel en su interior.
    /// El servidor extrae los Excel del ZIP y los procesa en background.
    /// Límite: 200 MB.
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
    /// Consulta el estado actual de un job de procesamiento.
    /// Los usuarios no-Admin solo pueden ver sus propios jobs.
    /// </summary>
    [HttpGet("status/{jobId:guid}")]
    public async Task<IActionResult> GetStatus(Guid jobId)
    {
        var job = await _jobRepo.GetByIdAsync(jobId);
        if (job == null) return NotFound(new { error = "Job not found." });

        if (!User.IsInRole("Admin") &&
            !job.CreatedByEmail.Equals(GetCurrentEmail(), StringComparison.OrdinalIgnoreCase))
            return Forbid();

        return Ok(new JobStatusResponse(
            job.Id, job.Status, job.TotalFiles, job.ProcessedFiles,
            job.ErrorMessage, job.ReportId, job.CreatedAt, job.CompletedAt, job.CreatedByEmail));
    }

    /// <summary>
    /// Lista el historial de jobs del usuario actual (Admin ve todos).
    /// Ordenado por fecha de creación descendente.
    /// </summary>
    [HttpGet("jobs")]
    public async Task<IActionResult> GetJobs()
    {
        var email = GetCurrentEmail();
        var jobs = User.IsInRole("Admin")
            ? await _jobRepo.GetAllAsync()
            : await _jobRepo.GetByEmailAsync(email);

        return Ok(jobs
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new JobStatusResponse(
                j.Id, j.Status, j.TotalFiles, j.ProcessedFiles,
                j.ErrorMessage, j.ReportId, j.CreatedAt, j.CompletedAt, j.CreatedByEmail)));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Extrae el email del usuario autenticado del JWT claim.</summary>
    private string GetCurrentEmail() =>
        User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? string.Empty;

    /// <summary>
    /// Valida extensión y tamaño de cada archivo.
    /// Retorna un mensaje de error descriptivo en el primer problema encontrado, o <c>null</c> si todo es válido.
    /// </summary>
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
