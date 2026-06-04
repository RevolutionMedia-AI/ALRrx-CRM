namespace Slice.Application.Interfaces;

/// <summary>
/// Orchestrates the full file-processing pipeline (save → parse → merge → export).
/// Each method returns a job ID immediately and runs the rest of the work in the background.
/// </summary>
public interface IFileProcessingOrchestrator
{
    /// <summary>
    /// Enqueues a batch of Excel file streams for parallel async processing.
    /// Streams are copied to temp files before this method returns.
    /// </summary>
    /// <returns>The ID of the <see cref="Slice.Domain.Entities.ProcessingJob"/> to poll for status.</returns>
    Task<Guid> EnqueueAsync(
        IReadOnlyList<Stream> fileStreams,
        IReadOnlyList<string> fileNames,
        string ownerEmail,
        CancellationToken ct = default);

    /// <summary>
    /// Enqueues a ZIP archive for extraction followed by parallel Excel processing.
    /// The ZIP stream is copied to a temp file before this method returns.
    /// </summary>
    /// <returns>The ID of the <see cref="Slice.Domain.Entities.ProcessingJob"/> to poll for status.</returns>
    Task<Guid> EnqueueZipAsync(Stream zipStream, string ownerEmail, CancellationToken ct = default);
}
