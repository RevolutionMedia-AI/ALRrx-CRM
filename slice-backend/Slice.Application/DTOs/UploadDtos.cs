using Slice.Domain.Enums;

namespace Slice.Application.DTOs;

/// <summary>
/// Respuesta inmediata al subir archivos. El cliente debe usar
/// <see cref="JobId"/> para hacer polling en <c>GET /api/fileupload/status/{jobId}</c>.
/// </summary>
/// <param name="JobId">Identificador único del job de procesamiento creado.</param>
/// <param name="FileCount">Número de archivos recibidos en esta subida.</param>
/// <param name="Status">Estado inicial del job (<c>"Processing"</c> o <c>"Extracting"</c>).</param>
public record UploadJobResponse(Guid JobId, int FileCount, string Status);

/// <summary>
/// Estado actual de un job de procesamiento, devuelto por el endpoint de polling.
/// </summary>
/// <param name="JobId">Identificador del job.</param>
/// <param name="Status">Etapa actual del pipeline (ver <see cref="JobStatus"/>).</param>
/// <param name="TotalFiles">Total de archivos Excel a procesar en el batch.</param>
/// <param name="ProcessedFiles">Archivos completamente procesados hasta ahora.</param>
/// <param name="ErrorMessage">Mensaje de error si <paramref name="Status"/> es <see cref="JobStatus.Failed"/>; de lo contrario <c>null</c>.</param>
/// <param name="ReportId">ID del reporte generado, disponible solo cuando <paramref name="Status"/> es <see cref="JobStatus.Completed"/>.</param>
/// <param name="CreatedAt">Cuándo se creó el job (UTC).</param>
/// <param name="CompletedAt">Cuándo terminó el job (UTC), o <c>null</c> si aún está en progreso.</param>
public record JobStatusResponse(
    Guid       JobId,
    JobStatus  Status,
    int        TotalFiles,
    int        ProcessedFiles,
    string?    ErrorMessage,
    string?    ReportId,
    DateTime   CreatedAt,
    DateTime?  CompletedAt);
