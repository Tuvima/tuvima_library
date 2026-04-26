using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

public interface ITextTrackRepository
{
    Task<IReadOnlyList<TextTrack>> GetByAssetAsync(Guid assetId, TextTrackKind? kind = null, CancellationToken ct = default);

    Task<TextTrack?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<TextTrack?> GetPreferredAsync(Guid assetId, TextTrackKind kind, string? language = null, CancellationToken ct = default);

    Task UpsertAsync(TextTrack track, CancellationToken ct = default);

    Task SetPreferredAsync(Guid id, CancellationToken ct = default);
}
