using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Models;
using MediaEngine.Contracts.Settings;
using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Branding;
using MediaEngine.Web.Services.Integration.Clients;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaEngine.Web.Services.Integration;

public sealed partial class EngineApiClient
{
    // -- GET /collections/search -----------------------------------------------------

    public async Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default)
    {
        var endpoint = "GET /collections/search";
        try
        {
            var encoded = WebUtility.UrlEncode(query);
            var response = await _http.GetAsync($"/collections/search?q={encoded}", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return [];
            }

            var raw = await response.Content.ReadFromJsonAsync<List<SearchRawResult>>(cancellationToken: ct);
            ClearFailure(endpoint);
            return raw?.Select(r => new SearchResultViewModel
            {
                WorkId         = r.WorkId,
                CollectionId          = r.CollectionId,
                Title          = r.Title,
                Author         = r.Author,
                MediaType      = r.MediaType,
                CollectionDisplayName = r.CollectionDisplayName,
                Series = r.Series,
                SeriesPosition = r.SeriesPosition,
                ShowName = r.ShowName,
                SeasonNumber = r.SeasonNumber,
                EpisodeNumber = r.EpisodeNumber,
            }).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/search failed");
            RecordExceptionFailure(endpoint, ex);
            return [];
        }
    }

    // -- Metadata search (/metadata/search) --------------------------------

    public async Task<List<MetadataSearchResultDto>> SearchMetadataAsync(
        string providerName, string query, string? mediaType = null,
        int limit = 25, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                provider_name = providerName,
                query,
                media_type = mediaType,
                limit,
            };
            var resp = await _http.PostAsJsonAsync("/metadata/search", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /metadata/search returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return [];
            }
            var raw = await resp.Content.ReadFromJsonAsync<MetadataSearchRaw>(ct);
            return raw?.Results?.Select(r => new MetadataSearchResultDto
            {
                Title          = r.Title,
                Author         = r.Author,
                Description    = r.Description,
                Year           = r.Year,
                ThumbnailUrl   = r.ThumbnailUrl,
                ProviderItemId = r.ProviderItemId,
                Confidence     = r.Confidence,
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/search failed");
            LastError = ex.Message;
            return [];
        }
    }

    // -- Fan-out metadata search -----------------------------------------

    public async Task<FanOutSearchResponseViewModel?> SearchMetadataFanOutAsync(
        string query, string? mediaType = null, string? providerId = null,
        int maxResultsPerProvider = 5, CancellationToken ct = default)
    {
        try
        {
            var payload = new
            {
                query,
                media_type = mediaType,
                provider_id = providerId,
                max_results_per_provider = maxResultsPerProvider,
            };
            var response = await _http.PostAsJsonAsync("/metadata/search-all", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"search-all failed: {response.StatusCode}";
                return null;
            }
            return await response.Content.ReadFromJsonAsync<FanOutSearchResponseViewModel>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "SearchMetadataFanOutAsync failed");
            return null;
        }
    }

    // -- Search results cache --------------------------------------------

    public async Task<string?> GetSearchResultsCacheAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/metadata/{entityId}/search-cache", ct);
            if (!response.IsSuccessStatusCode) return null;
            var wrapper = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);
            return wrapper.TryGetProperty("results_json", out var rj) ? rj.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetSearchResultsCacheAsync failed for {EntityId}", entityId);
            return null;
        }
    }

    public async Task SaveSearchResultsCacheAsync(Guid entityId, string resultsJson, CancellationToken ct = default)
    {
        try
        {
            var payload = new { results_json = resultsJson };
            await _http.PutAsJsonAsync($"/metadata/{entityId}/search-cache", payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SaveSearchResultsCacheAsync failed for {EntityId}", entityId);
        }
    }


    // -- Canonical values ------------------------------------------------

    public async Task<List<CanonicalFieldViewModel>> GetCanonicalValuesAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/metadata/canonical/{entityId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"canonical values failed: {response.StatusCode}";
                return [];
            }
            return await response.Content.ReadFromJsonAsync<List<CanonicalFieldViewModel>>(cancellationToken: ct) ?? [];
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "GetCanonicalValuesAsync failed for {EntityId}", entityId);
            return [];
        }
    }

    // -- Cover from URL --------------------------------------------------

    public async Task<bool> ApplyCoverFromUrlAsync(
        Guid entityId, string imageUrl, CancellationToken ct = default)
    {
        try
        {
            var payload = new { image_url = imageUrl };
            var response = await _http.PostAsJsonAsync($"/metadata/{entityId}/cover-from-url", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                LastError = $"cover-from-url failed: {response.StatusCode}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "ApplyCoverFromUrlAsync failed for {EntityId}", entityId);
            return false;
        }
    }

    // -- Wikidata Aliases (/metadata/{qid}/aliases) ----------------------------

    public async Task<AliasesResponseDto?> GetAliasesAsync(string qid, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<AliasesResponseDto>($"metadata/{qid}/aliases", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "GET /metadata/{Qid}/aliases failed", qid);
            return null;
        }
    }

    // -- Collection shelves ---------------------------------------------------

    public async Task<List<ContentGroupViewModel>> GetContentGroupsAsync(CancellationToken ct = default)
    {
        try
        {
            var groups = await _http.GetFromJsonAsync<List<ContentGroupViewModel>>("/collections/content-groups", ct) ?? [];
            foreach (var group in groups)
            {
                if (group.CoverUrl is not null)
                    group.CoverUrl = AbsoluteUrl(group.CoverUrl);
                if (group.BackgroundUrl is not null)
                    group.BackgroundUrl = AbsoluteUrl(group.BackgroundUrl);
                if (group.BannerUrl is not null)
                    group.BannerUrl = AbsoluteUrl(group.BannerUrl);
                if (group.HeroUrl is not null)
                    group.HeroUrl = AbsoluteUrl(group.HeroUrl);
                if (group.LogoUrl is not null)
                    group.LogoUrl = AbsoluteUrl(group.LogoUrl);

                if (group.ArtistPhotoUrl is not null)
                    group.ArtistPhotoUrl = AbsoluteUrl(group.ArtistPhotoUrl);
                if (group.PersonPhotoUrl is not null)
                    group.PersonPhotoUrl = AbsoluteUrl(group.PersonPhotoUrl);
                foreach (var preview in group.PreviewItems)
                    preview.ImageUrl = AbsoluteUrl(preview.ImageUrl);
            }

            return groups;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/content-groups failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<ContentGroupViewModel>> GetSystemViewGroupsAsync(string? mediaType = null, string? groupField = null, CancellationToken ct = default)
    {
        try
        {
            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(mediaType))
                queryParts.Add($"mediaType={Uri.EscapeDataString(mediaType)}");
            if (!string.IsNullOrWhiteSpace(groupField))
                queryParts.Add($"groupField={Uri.EscapeDataString(groupField)}");
            var url = "/collections/system-views" + (queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "");
            var groups = await _http.GetFromJsonAsync<List<ContentGroupViewModel>>(url, ct) ?? [];
            foreach (var g in groups)
            {
                if (g.CoverUrl is not null)
                    g.CoverUrl = AbsoluteUrl(g.CoverUrl);
                if (g.BackgroundUrl is not null)
                    g.BackgroundUrl = AbsoluteUrl(g.BackgroundUrl);
                if (g.BannerUrl is not null)
                    g.BannerUrl = AbsoluteUrl(g.BannerUrl);
                if (g.HeroUrl is not null)
                    g.HeroUrl = AbsoluteUrl(g.HeroUrl);
                if (g.LogoUrl is not null)
                    g.LogoUrl = AbsoluteUrl(g.LogoUrl);
                if (g.ArtistPhotoUrl is not null)
                    g.ArtistPhotoUrl = AbsoluteUrl(g.ArtistPhotoUrl);
                if (g.PersonPhotoUrl is not null)
                    g.PersonPhotoUrl = AbsoluteUrl(g.PersonPhotoUrl);
                foreach (var preview in g.PreviewItems)
                    preview.ImageUrl = AbsoluteUrl(preview.ImageUrl);
            }
            return groups;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/system-views failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<CollectionItemViewModel>> GetCollectionItemsAsync(Guid collectionId, int limit = 20, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/items?limit={limit}", profileId);
            var items = await _http.GetFromJsonAsync<List<CollectionItemViewModel>>(url, ct) ?? [];
            foreach (var item in items)
            {
                if (item.CoverUrl is not null)
                    item.CoverUrl = AbsoluteUrl(item.CoverUrl);
            }

            return items;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/items failed", collectionId);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<CollectionMediaLookupItemViewModel>> LookupCollectionMediaAsync(
        string? query,
        Guid? collectionId = null,
        string? mediaTypes = null,
        int offset = 0,
        int limit = 24,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var parameters = new List<string>
            {
                $"offset={Math.Max(0, offset)}",
                $"limit={Math.Clamp(limit, 1, 100)}",
            };

            if (!string.IsNullOrWhiteSpace(query))
                parameters.Add($"q={Uri.EscapeDataString(query.Trim())}");
            if (collectionId.HasValue)
                parameters.Add($"collectionId={collectionId.Value:D}");
            if (!string.IsNullOrWhiteSpace(mediaTypes))
                parameters.Add($"mediaTypes={Uri.EscapeDataString(mediaTypes)}");

            var url = $"/collections/media-lookup?{string.Join("&", parameters)}";
            url = AppendCollectionProfileQuery(url, profileId);
            var items = await _http.GetFromJsonAsync<List<CollectionMediaLookupItemViewModel>>(url, ct) ?? [];
            foreach (var item in items)
            {
                if (item.ArtworkUrl is not null)
                    item.ArtworkUrl = AbsoluteUrl(item.ArtworkUrl);
            }

            return items;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/media-lookup failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<bool> AddCollectionItemAsync(Guid collectionId, Guid workId, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/items", profileId);
            var resp = await _http.PostAsJsonAsync(url, new { work_id = workId }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /collections/{CollectionId}/items failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> RemoveCollectionItemAsync(Guid collectionId, Guid itemId, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/items/{itemId}", profileId);
            var resp = await _http.DeleteAsync(url, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /collections/{CollectionId}/items/{ItemId} failed", collectionId, itemId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> ReorderCollectionItemsAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/items/reorder", profileId);
            var resp = await _http.PutAsJsonAsync(url, new { item_ids = itemIds }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /collections/{CollectionId}/items/reorder failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UpdateCollectionEnabledAsync(Guid collectionId, bool enabled, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"/collections/{collectionId}/enabled", new { enabled }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /collections/{CollectionId}/enabled failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UpdateCollectionFeaturedAsync(Guid collectionId, bool featured, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"/collections/{collectionId}/featured", new { featured }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /collections/{CollectionId}/featured failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<CollectionPreviewResult?> PreviewCollectionRulesAsync(
        List<CollectionRulePredicateViewModel> rules, string matchMode, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var body = new { rules = rules.Select(r => new { field = r.Field, op = r.Op, value = r.Value, values = r.Values }).ToList(), match_mode = matchMode, limit };
            var response = await _http.PostAsJsonAsync("/collections/preview", body, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<CollectionPreviewResult>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /collections/preview failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> CreateCollectionAsync(
        string name,
        string? description,
        string? iconName,
        string collectionType,
        List<CollectionRulePredicateViewModel> rules,
        string matchMode,
        string? sortField,
        string sortDirection,
        bool liveUpdating,
        string visibility,
        Guid? profileId = null,
        CancellationToken ct = default)
        => await CreateCollectionAndReturnIdAsync(name, description, iconName, collectionType, rules, matchMode, sortField, sortDirection, liveUpdating, visibility, profileId, ct) is not null;

    public async Task<Guid?> CreateCollectionAndReturnIdAsync(
        string name,
        string? description,
        string? iconName,
        string collectionType,
        List<CollectionRulePredicateViewModel> rules,
        string matchMode,
        string? sortField,
        string sortDirection,
        bool liveUpdating,
        string visibility,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                name,
                description,
                icon_name = iconName,
                visibility,
                collection_type = collectionType,
                rules = rules.Select(r => new { field = r.Field, op = r.Op, value = r.Value, values = r.Values }).ToList(),
                match_mode = matchMode,
                sort_field = sortField,
                sort_direction = sortDirection,
                live_updating = liveUpdating,
            };
            var url = AppendCollectionProfileQuery("/collections", profileId);
            var response = await _http.PostAsJsonAsync(url, body, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            return result.TryGetProperty("id", out var idProperty) && Guid.TryParse(idProperty.GetString(), out var id)
                ? id
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /collections failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> UpdateCollectionAsync(
        Guid collectionId,
        string? name,
        string? description,
        string? iconName,
        List<CollectionRulePredicateViewModel>? rules,
        string? matchMode,
        string? visibility,
        string? sortField,
        string? sortDirection,
        bool? liveUpdating,
        bool? isEnabled,
        bool? isFeatured,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                name,
                description,
                icon_name = iconName,
                visibility,
                rules = rules?.Select(r => new { field = r.Field, op = r.Op, value = r.Value, values = r.Values }).ToList(),
                match_mode = matchMode,
                sort_field = sortField,
                sort_direction = sortDirection,
                live_updating = liveUpdating,
                is_enabled = isEnabled,
                is_featured = isFeatured,
            };
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}", profileId);
            var response = await _http.PutAsJsonAsync(url, body, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /collections/{CollectionId} failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UploadCollectionSquareArtworkAsync(
        Guid collectionId,
        Stream fileStream,
        string fileName,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetImageContentType(fileName));
            content.Add(fileContent, "file", fileName);

            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/square-artwork", profileId);
            var response = await _http.PostAsync(url, content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /collections/{CollectionId}/square-artwork failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

}
