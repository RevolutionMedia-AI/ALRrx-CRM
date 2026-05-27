using Slice.Domain.Enums;

namespace Slice.Domain.Entities;

public sealed class ProcessingJob
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public string CreatedByEmail { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string? ErrorMessage { get; set; }
    public string? MergedFilePath { get; set; }
    public List<string> SourceFiles { get; set; } = [];
    public string? ReportId { get; set; }
}
