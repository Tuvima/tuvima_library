using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface IPlaybackSegmentRepository
{
    Task<IReadOnlyList<PlaybackSegment>> ListByAssetAsync(Guid assetId, CancellationToken ct = default);
    Task<PlaybackSegment?> FindByIdAsync(Guid segmentId, CancellationToken ct = default);
    Task UpsertBatchAsync(Guid assetId, IReadOnlyList<PlaybackSegment> segments, CancellationToken ct = default);
    Task UpdateAsync(PlaybackSegment segment, CancellationToken ct = default);
    Task DeleteAsync(Guid segmentId, CancellationToken ct = default);
}
