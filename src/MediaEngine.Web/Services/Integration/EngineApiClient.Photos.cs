using MediaEngine.Contracts.Photos;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public async Task<PhotoPageDto?> GetPhotosAsync(
        int offset = 0, int limit = 120, string? search = null, bool favorites = false,
        bool includeHidden = false, Guid? albumId = null, CancellationToken ct = default)
    {
        try
        {
            var query = new Dictionary<string, string?>
            {
                ["offset"] = Math.Max(0, offset).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["limit"] = Math.Clamp(limit, 1, 500).ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["q"] = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                ["favorites"] = favorites ? "true" : null,
                ["hidden"] = includeHidden ? "true" : null,
                ["album"] = albumId?.ToString("D"),
            };
            return await GetAsync<PhotoPageDto>("GET /photos", "/photos", query, ct: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /photos failed");
            return null;
        }
    }

    public Task<PhotoScanResultDto?> ScanPhotosAsync(CancellationToken ct = default) =>
        PostAsync<ScanPhotoLibrariesRequest, PhotoScanResultDto>(
            "POST /photos/scan", "/photos/scan", new ScanPhotoLibrariesRequest(), ct: ct);

    public Task<bool> SetPhotoFavoriteAsync(Guid id, bool value, CancellationToken ct = default) =>
        SetPhotoFlagAsync(id, "favorite", value, ct);

    public Task<bool> SetPhotoHiddenAsync(Guid id, bool value, CancellationToken ct = default) =>
        SetPhotoFlagAsync(id, "hidden", value, ct);

    private Task<bool> SetPhotoFlagAsync(Guid id, string flag, bool value, CancellationToken ct) =>
        PutAsync(
            $"PUT /photos/{id:D}/{flag}",
            $"/photos/{id:D}/{flag}",
            new SetPhotoFlagRequest(value),
            ct: ct);

    public async Task<IReadOnlyList<PhotoAlbumDto>> GetPhotoAlbumsAsync(CancellationToken ct = default)
    {
        try
        {
            return await GetAsync<List<PhotoAlbumDto>>(
                "GET /photos/albums", "/photos/albums", static () => [], ct: ct);
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /photos/albums failed");
            return [];
        }
    }

    public Task<PhotoAlbumDto?> CreatePhotoAlbumAsync(
        string name, string? description = null, CancellationToken ct = default) =>
        PostAsync<CreatePhotoAlbumRequest, PhotoAlbumDto>(
            "POST /photos/albums",
            "/photos/albums",
            new CreatePhotoAlbumRequest(name, description),
            ct: ct);

    public async Task<int> AddPhotosToAlbumAsync(
        Guid albumId, IReadOnlyList<Guid> photoIds, CancellationToken ct = default)
    {
        try
        {
            var result = await PostAsync<AddPhotoAlbumItemsRequest, AddPhotoAlbumItemsResult>(
                $"POST /photos/albums/{albumId:D}/items",
                $"/photos/albums/{albumId:D}/items",
                new AddPhotoAlbumItemsRequest(photoIds),
                ct: ct);
            return result?.Added ?? 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /photos/albums/{AlbumId}/items failed", albumId);
            return 0;
        }
    }
}
