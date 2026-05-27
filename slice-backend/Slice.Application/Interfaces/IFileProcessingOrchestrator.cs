namespace Slice.Application.Interfaces;

public interface IFileProcessingOrchestrator
{
    /// <summary>
    /// Queues a batch of Excel files (or a ZIP) for async parallel processing.
    /// Returns a jobId to track progress.
    /// </summary>
    Task<Guid> EnqueueAsync(IReadOnlyList<Stream> fileStreams, IReadOnlyList<string> fileNames, string ownerEmail, CancellationToken ct = default);

    Task<Guid> EnqueueZipAsync(Stream zipStream, string ownerEmail, CancellationToken ct = default);
}
