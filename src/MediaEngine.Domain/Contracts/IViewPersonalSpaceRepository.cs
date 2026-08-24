using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Contracts;

public interface IViewPersonalSpaceRepository
{
    Task<ViewPersonalSpace?> GetByOwnerAsync(Guid ownerProfileId, CancellationToken ct = default);
    Task<ViewPersonalSpace?> GetByLibraryAsync(Guid libraryId, CancellationToken ct = default);
    Task<ViewPersonalSpace> CreateAsync(Guid ownerProfileId, Guid libraryId, CancellationToken ct = default);
    Task<IReadOnlyList<ViewSource>> GetSourcesAsync(Guid personalSpaceId, CancellationToken ct = default);
    Task<ViewSource> UpsertSourceAsync(ViewSource source, CancellationToken ct = default);
    Task<IReadOnlyList<ViewDevice>> GetDevicesAsync(Guid personalSpaceId, CancellationToken ct = default);
    Task<ViewDevice> UpsertDeviceAsync(ViewDevice device, CancellationToken ct = default);
}
