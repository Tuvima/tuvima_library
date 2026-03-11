using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Processors;

/// <summary>
/// WhisperSync alignment service — Phase 5 stub.
/// Creates and manages ebook-to-audiobook alignment jobs.
/// Actual Whisper inference is stubbed with NotImplementedException.
/// </summary>
public sealed class WhisperSyncService : IWhisperSyncService
{
    private readonly IAlignmentJobRepository _repo;
    private readonly ILogger<WhisperSyncService> _logger;

    public WhisperSyncService(
        IAlignmentJobRepository repo,
        ILogger<WhisperSyncService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<AlignmentJob> CreateAlignmentJobAsync(
        Guid ebookAssetId,
        Guid audiobookAssetId,
        CancellationToken ct = default)
    {
        var job = new AlignmentJob
        {
            Id = Guid.NewGuid(),
            EbookAssetId = ebookAssetId,
            AudiobookAssetId = audiobookAssetId,
            Status = AlignmentJobStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _repo.InsertAsync(job, ct);
        _logger.LogInformation(
            "Created WhisperSync alignment job {JobId} for ebook {EbookId} + audiobook {AudioId}",
            job.Id, ebookAssetId, audiobookAssetId);

        return job;
    }

    public Task<AlignmentJob?> GetJobAsync(Guid jobId, CancellationToken ct = default)
        => _repo.FindByIdAsync(jobId, ct);

    public Task<IReadOnlyList<AlignmentJob>> GetJobsForAssetAsync(
        Guid ebookAssetId,
        CancellationToken ct = default)
        => _repo.ListByAssetAsync(ebookAssetId, ct);

    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _repo.FindByIdAsync(jobId, ct);
        if (job is null) return false;

        if (job.Status is AlignmentJobStatus.Completed or AlignmentJobStatus.Failed)
        {
            _logger.LogWarning("Cannot cancel job {JobId} — already {Status}", jobId, job.Status);
            return false;
        }

        await _repo.UpdateStatusAsync(jobId, AlignmentJobStatus.Failed, null, "Cancelled by user", ct);
        _logger.LogInformation("Cancelled WhisperSync alignment job {JobId}", jobId);
        return true;
    }

    public async Task<bool> ProcessNextPendingAsync(CancellationToken ct = default)
    {
        var job = await _repo.FindPendingAsync(ct);
        if (job is null) return false;

        _logger.LogInformation("Processing WhisperSync alignment job {JobId}", job.Id);
        await _repo.UpdateStatusAsync(job.Id, AlignmentJobStatus.Processing, null, null, ct);

        try
        {
            // ── Phase 5 Stub ──────────────────────────────────────────────
            // Steps that will be implemented when Whisper inference is ready:
            // 1. Extract 16kHz mono WAV from audiobook via IFFmpegService.RunAsync()
            // 2. Get chapter text from IEpubContentService
            // 3. Run Whisper inference on WAV segments
            // 4. Align Whisper transcript with chapter text
            // 5. Store alignment data as JSON in alignment_jobs.alignment_data
            //
            // For now: set status to Failed with a descriptive message.
            // ──────────────────────────────────────────────────────────────

            await _repo.UpdateStatusAsync(
                job.Id,
                AlignmentJobStatus.Failed,
                alignmentData: null,
                errorMessage: "Whisper inference not yet implemented (Phase 5 stub). " +
                              "Infrastructure is ready — awaiting Whisper.net model integration.",
                ct);

            _logger.LogWarning(
                "WhisperSync job {JobId} completed as stub (inference not implemented)", job.Id);

            return true;
        }
        catch (Exception ex)
        {
            await _repo.UpdateStatusAsync(
                job.Id, AlignmentJobStatus.Failed, null, ex.Message, ct);
            _logger.LogError(ex, "WhisperSync job {JobId} failed with error", job.Id);
            return true;
        }
    }
}
