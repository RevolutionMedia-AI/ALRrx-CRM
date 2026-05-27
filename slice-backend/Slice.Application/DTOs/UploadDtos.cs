using Slice.Domain.Enums;

namespace Slice.Application.DTOs;

public record UploadJobResponse(Guid JobId, int FileCount, string Status);

public record JobStatusResponse(
    Guid JobId,
    JobStatus Status,
    int TotalFiles,
    int ProcessedFiles,
    string? ErrorMessage,
    string? ReportId,
    DateTime CreatedAt,
    DateTime? CompletedAt);
