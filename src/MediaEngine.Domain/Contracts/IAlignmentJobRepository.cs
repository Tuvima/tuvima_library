using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for WhisperSync alignment jobs.
/// </summary>
public interface IAlignmentJobRepository
{
    Task<AlignmentJob?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<AlignmentJob>> ListByAssetAsync(Guid ebookAssetId, CancellationToken ct = default);
    Task<AlignmentJob?> FindPendingAsync(CancellationToken ct = default);
    Task InsertAsync(AlignmentJob job, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, AlignmentJobStatus status, string? alignmentData, string? errorMessage, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
