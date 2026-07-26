using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

public interface ICollectionPlacementRepository
{
    Task<IReadOnlyList<CollectionPlacement>> GetByCollectionIdAsync(Guid collectionId, CancellationToken ct = default);
    Task<IReadOnlyList<CollectionPlacement>> GetByLocationAsync(string location, CancellationToken ct = default);
    Task UpsertAsync(CollectionPlacement placement, CancellationToken ct = default);
    Task DeleteAsync(Guid placementId, CancellationToken ct = default);
    Task DeleteByCollectionIdAsync(Guid collectionId, CancellationToken ct = default);
}
