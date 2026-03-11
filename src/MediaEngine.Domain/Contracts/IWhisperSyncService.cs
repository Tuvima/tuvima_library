using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Service contract for WhisperSync ebook-to-audiobook alignment.
/// Phase 5 stub — actual Whisper inference is not yet implemented.
/// </summary>
public interface IWhisperSyncService
{
    /// <summary>Create a new alignment job for an ebook/audiobook pair.</summary>
    Task<AlignmentJob> CreateAlignmentJobAsync(
        Guid ebookAssetId,
        Guid audiobookAssetId,
        CancellationToken ct = default);

    /// <summary>Get an alignment job by ID.</summary>
    Task<AlignmentJob?> GetJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Get all alignment jobs for an ebook asset.</summary>
    Task<IReadOnlyList<AlignmentJob>> GetJobsForAssetAsync(
        Guid ebookAssetId,
        CancellationToken ct = default);

    /// <summary>Cancel a pending or processing alignment job.</summary>
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>Process the next pending alignment job. Returns true if a job was processed.</summary>
    Task<bool> ProcessNextPendingAsync(CancellationToken ct = default);
}
