namespace Slice.Domain.Enums;

/// <summary>
/// Represents the lifecycle stages of a <see cref="Slice.Domain.Entities.ProcessingJob"/>.
/// The flow is: Pending → Extracting (ZIP only) → Processing → Merging → Completed.
/// Any stage can transition to Failed.
/// </summary>
public enum JobStatus
{
    /// <summary>Job has been created and is waiting to start.</summary>
    Pending,

    /// <summary>ZIP archive is being extracted to temp storage. Only applies to ZIP uploads.</summary>
    Extracting,

    /// <summary>Excel files are being parsed in parallel.</summary>
    Processing,

    /// <summary>Parsed reports are being merged and exported to XLSX/CSV.</summary>
    Merging,

    /// <summary>All processing finished successfully. The report is available.</summary>
    Completed,

    /// <summary>An error occurred; see <see cref="Slice.Domain.Entities.ProcessingJob.ErrorMessage"/> for details.</summary>
    Failed,
}
