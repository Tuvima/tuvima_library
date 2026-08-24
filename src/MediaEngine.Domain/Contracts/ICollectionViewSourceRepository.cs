using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Contracts;

public interface ICollectionViewSourceRepository
{
    Task<CollectionViewSource> AddGalleryAsync(
        AddCollectionGallerySourceCommand command,
        CancellationToken ct = default);
    Task<CollectionViewSource> AddSmartRuleAsync(
        AddCollectionViewRuleSourceCommand command,
        CancellationToken ct = default);
    Task<IReadOnlyList<CollectionViewSource>> ListAsync(
        Guid collectionId,
        Guid ownerProfileId,
        CancellationToken ct = default);
    Task<CollectionViewSource?> UpdateAsync(
        UpdateCollectionViewSourceCommand command,
        CancellationToken ct = default);
    Task<bool> RemoveAsync(
        Guid collectionId,
        Guid sourceId,
        Guid ownerProfileId,
        CancellationToken ct = default);
    Task<IReadOnlyList<CollectionViewSourceProjection>> GetAuthorizedProjectionAsync(
        IReadOnlyCollection<Guid> collectionIds,
        Guid viewerProfileId,
        CancellationToken ct = default);
}
