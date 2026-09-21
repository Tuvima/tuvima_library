using System.Net.Http.Json;
using MediaEngine.Contracts.Artwork;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public async Task<ArtworkBrowsePageDto?> GetArtworkLibraryAsync(
        string? entityKind = null,
        string? artworkType = null,
        string? search = null,
        string? browseAs = null,
        string? mediaType = null,
        string? artworkState = null,
        string? sort = null,
        int offset = 0,
        int limit = 48,
        CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["entityKind"] = entityKind,
            ["artworkType"] = artworkType,
            ["search"] = search,
            ["browseAs"] = browseAs,
            ["mediaType"] = mediaType,
            ["artworkState"] = artworkState,
            ["sort"] = sort,
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var page = await GetAsync<ArtworkBrowsePageDto>(
            "GET /api/v1/display/artwork", "/api/v1/display/artwork", query, ct: ct);
        return page is null
            ? null
            : page with
            {
                Items = page.Items.Select(item => item with
                {
                    ImageUrl = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : AbsoluteUrl(item.ImageUrl),
                    BackgroundImageUrl = string.IsNullOrWhiteSpace(item.BackgroundImageUrl) ? null : AbsoluteUrl(item.BackgroundImageUrl),
                    LogoImageUrl = string.IsNullOrWhiteSpace(item.LogoImageUrl) ? null : AbsoluteUrl(item.LogoImageUrl),
                    PreviewItems = item.PreviewItems.Select(preview => preview with
                    {
                        ImageUrl = AbsoluteUrl(preview.ImageUrl),
                    }).ToList(),
                }).ToList(),
            };
    }

    public async Task<ArtworkAssetPageDto?> GetArtworkAssetsAsync(
        string? search = null, string? role = null, string? aspect = null,
        Guid? targetEntityId = null, int offset = 0, int limit = 48, CancellationToken ct = default)
    {
        var query = new Dictionary<string, string?>
        {
            ["search"] = search,
            ["role"] = role,
            ["aspect"] = aspect,
            ["targetEntityId"] = targetEntityId?.ToString("D"),
            ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var page = await GetAsync<ArtworkAssetPageDto>(
            "GET /api/v1/display/artwork/assets", "/api/v1/display/artwork/assets", query, ct: ct);
        return page is null ? null : page with { Items = page.Items.Select(NormalizeAsset).ToList() };
    }

    public async Task<IReadOnlyList<ArtworkLibraryItemDto>> GetUniverseArtworkHierarchyAsync(
        Guid collectionId, CancellationToken ct = default)
    {
        var items = await GetAsync<List<ArtworkLibraryItemDto>>(
            "GET /api/v1/display/artwork/universes/{collectionId}/entities",
            $"/api/v1/display/artwork/universes/{collectionId:D}/entities", ct: ct) ?? [];
        return items.Select(item => item with
        {
            ImageUrl = string.IsNullOrWhiteSpace(item.ImageUrl) ? null : AbsoluteUrl(item.ImageUrl),
            BackgroundImageUrl = string.IsNullOrWhiteSpace(item.BackgroundImageUrl) ? null : AbsoluteUrl(item.BackgroundImageUrl),
            LogoImageUrl = string.IsNullOrWhiteSpace(item.LogoImageUrl) ? null : AbsoluteUrl(item.LogoImageUrl),
        }).ToList();
    }

    public async Task<ArtworkEntityWorkspaceDto?> GetEntityArtworkAsync(
        string entityType, Guid entityId, CancellationToken ct = default)
    {
        var result = await GetAsync<ArtworkEntityWorkspaceDto>(
            "GET /api/v1/display/artwork/entities/{entityType}/{entityId}",
            $"/api/v1/display/artwork/entities/{Uri.EscapeDataString(entityType)}/{entityId:D}", ct: ct);
        return result is null ? null : NormalizeWorkspace(result);
    }

    public async Task<ArtworkEntityWorkspaceDto?> LinkArtworkAssetAsync(
        string entityType, Guid entityId, ArtworkLinkRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/display/artwork/entities/{Uri.EscapeDataString(entityType)}/{entityId:D}/links", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<ArtworkEntityWorkspaceDto>(cancellationToken: ct);
        return result is null ? null : NormalizeWorkspace(result);
    }

    public async Task<ArtworkAssetDto?> AddArtworkFromUrlAsync(
        string entityType, Guid entityId, ArtworkFromUrlRequest request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"/api/v1/display/artwork/entities/{Uri.EscapeDataString(entityType)}/{entityId:D}/from-url", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<ArtworkAssetDto>(cancellationToken: ct);
        return result is null ? null : NormalizeAsset(result);
    }

    public async Task<ArtworkAssetDto?> UploadCanonicalArtworkAsync(
        string entityType, Guid entityId, string role, Stream stream, string fileName,
        string? entityLabel = null, string? mediaType = null, string? year = null,
        CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetImageContentType(fileName));
        content.Add(fileContent, "file", fileName);
        content.Add(new StringContent(role), "role");
        if (!string.IsNullOrWhiteSpace(entityLabel)) content.Add(new StringContent(entityLabel), "entityLabel");
        if (!string.IsNullOrWhiteSpace(mediaType)) content.Add(new StringContent(mediaType), "mediaType");
        if (!string.IsNullOrWhiteSpace(year)) content.Add(new StringContent(year), "year");
        var response = await _http.PostAsync(
            $"/api/v1/display/artwork/entities/{Uri.EscapeDataString(entityType)}/{entityId:D}/upload", content, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<ArtworkAssetDto>(cancellationToken: ct);
        return result is null ? null : NormalizeAsset(result);
    }

    public async Task<bool> RemoveArtworkLinkAsync(Guid linkId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/v1/display/artwork/links/{linkId:D}", ct);
        return response.IsSuccessStatusCode;
    }

    private ArtworkAssetDto NormalizeAsset(ArtworkAssetDto asset) => asset with
    {
        ContentUrl = AbsoluteUrl(asset.ContentUrl),
        ThumbnailUrl = AbsoluteUrl(asset.ThumbnailUrl),
    };

    private ArtworkEntityWorkspaceDto NormalizeWorkspace(ArtworkEntityWorkspaceDto workspace) => workspace with
    {
        Variants = workspace.Variants.Select(variant => variant with
        {
            ContentUrl = AbsoluteUrl(variant.ContentUrl),
            ThumbnailUrl = AbsoluteUrl(variant.ThumbnailUrl),
        }).ToList(),
    };
}
