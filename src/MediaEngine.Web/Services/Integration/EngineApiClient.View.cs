using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Domain.PersonalMedia;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    public Task<ViewScopeResolutionDto?> GetViewScopesAsync(ViewScopeKind? scope = null, Guid? scopeProfileId = null, CancellationToken ct = default) =>
        GetAsync<ViewScopeResolutionDto>("GET /view/scopes", "/view/scopes", new Dictionary<string, string?>
        {
            ["scope"] = scope.HasValue ? ScopeValue(scope.Value) : null,
            ["scopeProfileId"] = scopeProfileId?.ToString("D"),
        }, ct: ct);

    public Task<ViewPreferencesDto?> GetViewPreferencesAsync(CancellationToken ct = default) =>
        GetAsync<ViewPreferencesDto>("GET /view/preferences", "/view/preferences", ct: ct);

    public async Task<ViewPreferencesDto?> UpdateViewPreferencesAsync(ViewScopeKind scope, Guid? scopeProfileId, ViewTimelineDensity timelineDensity, CancellationToken ct = default)
    {
        const string endpoint = "PUT /view/preferences";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/view/preferences")
            {
                Content = JsonContent.Create(new ViewPreferencesRequest(ScopeValue(scope), scopeProfileId, timelineDensity)),
            };
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { await RecordHttpFailureAsync(endpoint, response, ct); return null; }
            ClearFailure(endpoint);
            return await response.Content.ReadFromJsonAsync<ViewPreferencesDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex) { RecordExceptionFailure(endpoint, ex, true); return null; }
    }

    public Task<ViewAssetTimelinePageDto?> GetViewAssetsAsync(ViewAssetQueryOptions options, CancellationToken ct = default)
    {
        var query = new List<string>();
        AddQuery(query, "scope", ScopeValue(options.Scope));
        AddQuery(query, "scopeProfileId", options.ScopeProfileId?.ToString("D"));
        AddQuery(query, "cursor", options.Cursor);
        AddQuery(query, "q", options.Search?.Trim());
        foreach (var kind in options.Kinds ?? []) AddQuery(query, "kind", kind);
        AddQuery(query, "favorite", options.FavoritesOnly ? "true" : null);
        AddQuery(query, "hidden", options.HiddenOnly ? "true" : null);
        AddQuery(query, "lifecycle", options.Lifecycle);
        AddQuery(query, "galleryId", options.GalleryId?.ToString("D"));
        AddQuery(query, "limit", Math.Clamp(options.Limit, 1, 500).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<ViewAssetTimelinePageDto>("GET /view/assets", $"/view/assets?{string.Join('&', query)}", ct: ct);
    }

    public Task<ViewPeoplePageDto?> GetViewPeopleAsync(ViewDiscoveryQueryOptions options, CancellationToken ct = default) =>
        GetViewDiscoveryAsync<ViewPeoplePageDto>("people", options, ct);

    public Task<ViewPlacesPageDto?> GetViewPlacesAsync(ViewDiscoveryQueryOptions options, CancellationToken ct = default) =>
        GetViewDiscoveryAsync<ViewPlacesPageDto>("places", options, ct);

    public async Task<ViewUploadResult> UploadViewMediaAsync(Stream fileStream, string fileName, string? contentType = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        try
        {
            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            if (MediaTypeHeaderValue.TryParse(contentType, out var mediaType)) fileContent.Headers.ContentType = mediaType;
            content.Add(fileContent, "file", Path.GetFileName(fileName));
            using var response = await _http.PostAsync("/view/uploads", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(false, ErrorMessage: await ReadViewErrorAsync(response, ct));
            var upload = await response.Content.ReadFromJsonAsync<ViewUploadResponseDto>(cancellationToken: ct);
            return upload is null ? new(false, ErrorMessage: "The Engine returned no upload result.") : new(true, upload);
        }
        catch (OperationCanceledException) { return new(false, ErrorMessage: "The upload was canceled."); }
        catch (Exception ex) { _logger.LogWarning(ex, "POST /view/uploads failed"); return new(false, ErrorMessage: "The Dashboard could not upload this file."); }
    }

    public Task<bool> SetViewItemFavoriteAsync(Guid itemId, bool value, CancellationToken ct = default) =>
        PutAsync("PUT /view/items/{id}/favorite", $"/view/items/{itemId:D}/favorite", new SetLocalAssetFlagRequest(value), ct: ct);
    public Task<bool> SetViewItemHiddenAsync(Guid itemId, bool value, CancellationToken ct = default) =>
        PutAsync("PUT /view/items/{id}/hidden", $"/view/items/{itemId:D}/hidden", new SetLocalAssetFlagRequest(value), ct: ct);
    public Task<bool> ArchiveViewItemAsync(Guid itemId, CancellationToken ct = default) => LifecycleAsync(itemId, "archive", ct);
    public Task<bool> TrashViewItemAsync(Guid itemId, CancellationToken ct = default) => LifecycleAsync(itemId, "trash", ct);
    public Task<bool> RestoreViewItemAsync(Guid itemId, CancellationToken ct = default) => LifecycleAsync(itemId, "restore", ct);

    public Task<ViewGalleryListResponse?> GetViewGalleriesAsync(CancellationToken ct = default) =>
        GetAsync<ViewGalleryListResponse>("GET /view/galleries", "/view/galleries", ct: ct);
    public Task<ViewGalleryDto?> GetViewGalleryAsync(Guid galleryId, CancellationToken ct = default) =>
        GetAsync<ViewGalleryDto>("GET /view/galleries/{id}", $"/view/galleries/{galleryId:D}", ct: ct);
    public Task<ViewGalleryDto?> CreateViewGalleryAsync(ViewGalleryRequest request, CancellationToken ct = default) =>
        PostAsync<ViewGalleryRequest, ViewGalleryDto>("POST /view/galleries", "/view/galleries", request, ct: ct);

    public async Task<ViewGalleryDto?> UpdateViewGalleryAsync(Guid galleryId, ViewGalleryRequest request, CancellationToken ct = default)
    {
        using var message = new HttpRequestMessage(HttpMethod.Put, $"/view/galleries/{galleryId:D}") { Content = JsonContent.Create(request) };
        using var response = await _http.SendAsync(message, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ViewGalleryDto>(cancellationToken: ct) : null;
    }

    public Task<bool> DeleteViewGalleryAsync(Guid galleryId, CancellationToken ct = default) =>
        DeleteAsync("DELETE /view/galleries/{id}", $"/view/galleries/{galleryId:D}", ct: ct);
    public Task<IReadOnlyList<ViewGalleryShareTargetDto>?> GetViewGalleryShareTargetsAsync(CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ViewGalleryShareTargetDto>>(
            "GET /view/share-targets", "/view/share-targets", ct: ct);
    public Task<IReadOnlyList<ViewGalleryShareDto>?> GetViewGallerySharesAsync(Guid galleryId, CancellationToken ct = default) =>
        GetAsync<IReadOnlyList<ViewGalleryShareDto>>(
            "GET /view/galleries/{id}/shares", $"/view/galleries/{galleryId:D}/shares", ct: ct);
    public Task<bool> ReplaceViewGallerySharesAsync(
        Guid galleryId,
        IReadOnlyCollection<ViewGalleryShareRequest> shares,
        CancellationToken ct = default) =>
        PutAsync(
            "PUT /view/galleries/{id}/shares",
            $"/view/galleries/{galleryId:D}/shares",
            new ViewGallerySharesRequest(shares),
            ct: ct);
    public Task<AddViewGalleryItemsResponseDto?> AddViewGalleryItemsAsync(Guid galleryId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default) =>
        PostAsync<ViewGalleryItemsRequest, AddViewGalleryItemsResponseDto>("POST /view/galleries/{id}/items", $"/view/galleries/{galleryId:D}/items", new ViewGalleryItemsRequest(itemIds), ct: ct);

    public async Task<ViewItemsRemovedResponse?> RemoveViewGalleryItemsAsync(Guid galleryId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/view/galleries/{galleryId:D}/items") { Content = JsonContent.Create(new ViewGalleryItemsRequest(itemIds)) };
        using var response = await _http.SendAsync(request, ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ViewItemsRemovedResponse>(cancellationToken: ct) : null;
    }

    public Task<ViewPersonalSpaceAdminReviewDto?> GetViewProfileSourcesAsync(
        Guid profileId,
        CancellationToken ct = default) =>
        GetAsync<ViewPersonalSpaceAdminReviewDto>(
            "GET /view/admin/profiles/{profileId}/sources",
            $"/view/admin/profiles/{profileId:D}/sources",
            ct: ct);

    private Task<bool> LifecycleAsync(Guid itemId, string action, CancellationToken ct) =>
        PostAsync("POST /view/items/{id}/lifecycle", $"/view/items/{itemId:D}/{action}", new { }, ct: ct);

    private Task<T?> GetViewDiscoveryAsync<T>(string resource, ViewDiscoveryQueryOptions options, CancellationToken ct)
    {
        var query = new List<string>();
        AddQuery(query, "scope", ScopeValue(options.Scope));
        AddQuery(query, "scopeProfileId", options.ScopeProfileId?.ToString("D"));
        AddQuery(query, "q", options.Search?.Trim());
        AddQuery(query, "cursor", options.Cursor);
        AddQuery(query, "limit", Math.Clamp(options.Limit, 1, 100).ToString(System.Globalization.CultureInfo.InvariantCulture));
        return GetAsync<T>($"GET /view/{resource}", $"/view/{resource}?{string.Join('&', query)}", ct: ct);
    }
    private static string ScopeValue(ViewScopeKind scope) => scope.ToString().ToLowerInvariant();

    private static async Task<string> ReadViewErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (document.RootElement.TryGetProperty("detail", out var detail) && !string.IsNullOrWhiteSpace(detail.GetString())) return detail.GetString()!;
            if (document.RootElement.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString())) return title.GetString()!;
        }
        catch (JsonException) { }
        return $"The Engine rejected the request ({(int)response.StatusCode}).";
    }
}
