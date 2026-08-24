using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Domain.Contracts;

public interface IViewGalleryRepository
{
    Task<ViewGallery?> GetAsync(Guid galleryId, CancellationToken ct = default);
    Task<IReadOnlyList<ViewGallery>> GetOwnedAsync(Guid ownerProfileId, CancellationToken ct = default);
    Task<IReadOnlyList<ViewGallery>> GetSharedWithAsync(Guid profileId, CancellationToken ct = default);
    Task<ViewGallery> CreateAsync(CreateViewGalleryCommand command, CancellationToken ct = default);
    Task<ViewGallery?> UpdateAsync(UpdateViewGalleryCommand command, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid galleryId, CancellationToken ct = default);
    Task<ViewGalleryItemPage> GetItemsAsync(
        Guid galleryId,
        int? afterPosition = null,
        Guid? afterItemId = null,
        int limit = 100,
        CancellationToken ct = default);
    Task<AddViewGalleryItemsResult> AddItemsAsync(
        Guid galleryId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default);
    Task<int> RemoveItemsAsync(Guid galleryId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
    Task<bool> SetItemPositionAsync(Guid galleryId, Guid itemId, int position, CancellationToken ct = default);
    Task ReplaceSharesAsync(
        Guid galleryId,
        IReadOnlyCollection<(Guid ProfileId, ViewGallerySharePermission Permission)> shares,
        CancellationToken ct = default);
    Task<IReadOnlyList<ViewGalleryShare>> GetSharesAsync(Guid galleryId, CancellationToken ct = default);
}
