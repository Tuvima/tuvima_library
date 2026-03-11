using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Persistence contract for EPUB reader text highlights.
/// </summary>
public interface IReaderHighlightRepository
{
    Task<IReadOnlyList<ReaderHighlight>> ListByAssetAsync(string userId, Guid assetId, CancellationToken ct = default);
    Task<ReaderHighlight?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task InsertAsync(ReaderHighlight highlight, CancellationToken ct = default);
    Task UpdateAsync(Guid id, string? color, string? noteText, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
