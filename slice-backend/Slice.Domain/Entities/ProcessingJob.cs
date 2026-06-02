using Slice.Domain.Enums;

namespace Slice.Domain.Entities;

/// <summary>
/// Tracks the lifecycle of a background file-processing batch.
/// Created when files are uploaded; polled by the client until <see cref="Status"/>
/// reaches <see cref="JobStatus.Completed"/> or <see cref="JobStatus.Failed"/>.
/// </summary>
public sealed class ProcessingJob
{
    /// <summary>Unique identifier returned to the client on upload.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Current stage of processing.</summary>
    public JobStatus Status { get; set; } = JobStatus.Pending;

    /// <summary>Email of the user who initiated the upload.</summary>
    public string CreatedByEmail { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the job was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the job reached a terminal state (Completed or Failed).</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Total number of Excel files to process in this batch.</summary>
    public int TotalFiles { get; set; }

    /// <summary>Number of files fully processed so far. Updated atomically during parallel parsing.</summary>
    public int ProcessedFiles { get; set; }

    /// <summary>Human-readable error message; set only when <see cref="Status"/> is <see cref="JobStatus.Failed"/>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Path to the merged output file (deprecated; use <see cref="ReportId"/> instead).</summary>
    public string? MergedFilePath { get; set; }

    /// <summary>Temp paths of the source Excel files while they are being processed.</summary>
    public List<string> SourceFiles { get; set; } = [];

    /// <summary>ID of the <see cref="SliceReport"/> produced on successful completion.</summary>
    public string? ReportId { get; set; }
}
