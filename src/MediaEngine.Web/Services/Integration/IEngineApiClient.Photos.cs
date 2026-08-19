using MediaEngine.Contracts.Photos;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    Task<PhotoPageDto?> GetPhotosAsync(
        int offset = 0, int limit = 120, string? search = null, bool favorites = false,
        bool includeHidden = false, Guid? albumId = null, CancellationToken ct = default);
    Task<PhotoScanResultDto?> ScanPhotosAsync(CancellationToken ct = default);
    Task<bool> SetPhotoFavoriteAsync(Guid id, bool value, CancellationToken ct = default);
    Task<bool> SetPhotoHiddenAsync(Guid id, bool value, CancellationToken ct = default);
    Task<IReadOnlyList<PhotoAlbumDto>> GetPhotoAlbumsAsync(CancellationToken ct = default);
    Task<PhotoAlbumDto?> CreatePhotoAlbumAsync(string name, string? description = null, CancellationToken ct = default);
    Task<int> AddPhotosToAlbumAsync(Guid albumId, IReadOnlyList<Guid> photoIds, CancellationToken ct = default);
}
