using MediaEngine.Domain.Entities;

namespace MediaEngine.Storage.Contracts;

public interface IHubPlacementRepository
{
    Task<IReadOnlyList<HubPlacement>> GetByHubIdAsync(Guid hubId, CancellationToken ct = default);
    Task<IReadOnlyList<HubPlacement>> GetByLocationAsync(string location, CancellationToken ct = default);
    Task UpsertAsync(HubPlacement placement, CancellationToken ct = default);
    Task DeleteAsync(Guid placementId, CancellationToken ct = default);
    Task DeleteByHubIdAsync(Guid hubId, CancellationToken ct = default);
}
