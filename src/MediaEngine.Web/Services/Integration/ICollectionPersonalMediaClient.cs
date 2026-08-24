using MediaEngine.Contracts.Collections;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Narrow transport boundary for the Collection editor. It deliberately lists
/// Gallery references instead of depending on the evolving View page contract.
/// </summary>
public interface ICollectionPersonalMediaClient
{
    string? LastError { get; }
    Task<IReadOnlyList<CollectionGalleryReferenceDto>> GetEligibleGalleriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPersonalMediaSourceDto>> GetSourcesAsync(Guid collectionId, CancellationToken ct = default);
    Task<CollectionPersonalMediaSourceDto?> AddSourceAsync(Guid collectionId, CollectionPersonalMediaSourceWriteRequest request, CancellationToken ct = default);
    Task<CollectionPersonalMediaSourceDto?> UpdateSourceAsync(Guid collectionId, Guid sourceId, CollectionPersonalMediaSourceWriteRequest request, CancellationToken ct = default);
    Task<bool> RemoveSourceAsync(Guid collectionId, Guid sourceId, CancellationToken ct = default);
}
