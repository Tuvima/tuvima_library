using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for EPUB reader statistics.
/// Uses upsert semantics — one row per (user_id, asset_id).
/// </summary>
public interface IReaderStatisticsRepository
{
    Task<ReaderStatistics?> GetAsync(string userId, Guid assetId, CancellationToken ct = default);
    Task UpsertAsync(ReaderStatistics stats, CancellationToken ct = default);
}
