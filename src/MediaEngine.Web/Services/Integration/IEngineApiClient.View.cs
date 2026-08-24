using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<ViewScopeResolutionDto?> GetViewScopesAsync(ViewScopeKind? scope = null, Guid? scopeProfileId = null, CancellationToken ct = default);
    Task<ViewPreferencesDto?> GetViewPreferencesAsync(CancellationToken ct = default);
    Task<ViewPreferencesDto?> UpdateViewPreferencesAsync(ViewScopeKind scope, Guid? scopeProfileId, ViewTimelineDensity timelineDensity, CancellationToken ct = default);
    Task<ViewAssetTimelinePageDto?> GetViewAssetsAsync(ViewAssetQueryOptions options, CancellationToken ct = default);
    Task<ViewPeoplePageDto?> GetViewPeopleAsync(ViewDiscoveryQueryOptions options, CancellationToken ct = default);
    Task<ViewPlacesPageDto?> GetViewPlacesAsync(ViewDiscoveryQueryOptions options, CancellationToken ct = default);
    Task<ViewUploadResult> UploadViewMediaAsync(Stream fileStream, string fileName, string? contentType = null, CancellationToken ct = default);
    Task<bool> SetViewItemFavoriteAsync(Guid itemId, bool value, CancellationToken ct = default);
    Task<bool> SetViewItemHiddenAsync(Guid itemId, bool value, CancellationToken ct = default);
    Task<bool> ArchiveViewItemAsync(Guid itemId, CancellationToken ct = default);
    Task<bool> TrashViewItemAsync(Guid itemId, CancellationToken ct = default);
    Task<bool> RestoreViewItemAsync(Guid itemId, CancellationToken ct = default);
    Task<ViewGalleryListResponse?> GetViewGalleriesAsync(CancellationToken ct = default);
    Task<ViewGalleryDto?> GetViewGalleryAsync(Guid galleryId, CancellationToken ct = default);
    Task<ViewGalleryDto?> CreateViewGalleryAsync(ViewGalleryRequest request, CancellationToken ct = default);
    Task<ViewGalleryDto?> UpdateViewGalleryAsync(Guid galleryId, ViewGalleryRequest request, CancellationToken ct = default);
    Task<bool> DeleteViewGalleryAsync(Guid galleryId, CancellationToken ct = default);
    Task<AddViewGalleryItemsResponseDto?> AddViewGalleryItemsAsync(Guid galleryId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
    Task<ViewItemsRemovedResponse?> RemoveViewGalleryItemsAsync(Guid galleryId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default);
}

public sealed record ViewAssetQueryOptions(
    ViewScopeKind Scope = ViewScopeKind.Shared,
    Guid? ScopeProfileId = null,
    string? Cursor = null,
    string? Search = null,
    IReadOnlyList<string>? Kinds = null,
    bool FavoritesOnly = false,
    bool HiddenOnly = false,
    string Lifecycle = "active",
    Guid? GalleryId = null,
    int Limit = 120);

public sealed record ViewUploadResult(bool Success, ViewUploadResponseDto? Upload = null, string? ErrorMessage = null);

public sealed record ViewDiscoveryQueryOptions(
    ViewScopeKind Scope = ViewScopeKind.Shared,
    Guid? ScopeProfileId = null,
    string? Search = null,
    string? Cursor = null,
    int Limit = 100);
