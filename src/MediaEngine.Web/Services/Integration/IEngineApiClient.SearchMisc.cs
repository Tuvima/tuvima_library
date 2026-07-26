using System.Text.Json;
using MediaEngine.Contracts.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Contracts.Paging;
using MediaEngine.Contracts.Playback;
using MediaEngine.Contracts.Settings;
using MediaEngine.Domain.Models;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

public partial interface IEngineApiClient
{
    /// <summary>GET /api/v1/display/search - ranked universal search across local media and entities.</summary>
    Task<UniversalSearchResponseDto?> GetUniversalSearchAsync(
        string query,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>GET /collections/search?q= — full-text search across all works (min 2 chars).</summary>
    Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default);

    // ── Metadata search (/metadata/search) ───────────────────────────────

    /// <summary>POST /metadata/search — multi-result metadata search against a specific provider.</summary>
    Task<List<MetadataSearchResultDto>> SearchMetadataAsync(
        string providerName, string query, string? mediaType = null,
        int limit = 25, CancellationToken ct = default);

    // -- Fan-out metadata search (/metadata/search-all) ------------------

    /// <summary>POST /metadata/search-all  -  fan-out search across all eligible providers.</summary>
    Task<FanOutSearchResponseViewModel?> SearchMetadataFanOutAsync(
        string query, string? mediaType = null, string? providerId = null,
        int maxResultsPerProvider = 5, CancellationToken ct = default);


    // ── Search results cache (/metadata/{entityId}/search-cache) ────────

    /// <summary>GET /metadata/{entityId}/search-cache — cached fan-out search results (30-day TTL).</summary>
    Task<string?> GetSearchResultsCacheAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>PUT /metadata/{entityId}/search-cache — store fan-out search results.</summary>
    Task SaveSearchResultsCacheAsync(Guid entityId, string resultsJson, CancellationToken ct = default);
    // -- Canonical values (/metadata/canonical/{entityId}) ---------------

    /// <summary>GET /metadata/canonical/{entityId}  -  get all canonical values with provenance.</summary>
    Task<List<CanonicalFieldViewModel>> GetCanonicalValuesAsync(
        Guid entityId, CancellationToken ct = default);

    // -- Cover from URL (/metadata/{entityId}/cover-from-url) ------------

    /// <summary>POST /metadata/{entityId}/cover-from-url  -  download cover from provider URL.</summary>
    Task<bool> ApplyCoverFromUrlAsync(
        Guid entityId, string imageUrl, CancellationToken ct = default);

    // ── Managed Collections (managed collections surface) ────────────────────────────────────────

    /// <summary>GET /collections/{collectionId}/group-detail — full drill-down view of a content group (album, TV show, book series, movie series).</summary>
    Task<CollectionGroupDetailViewModel?> GetCollectionGroupDetailAsync(Guid collectionId, CancellationToken ct = default);

    /// <summary>GET /collections/artist-detail-by-name?artistName=X — artist drill-down by name (system-view mode).</summary>
    Task<CollectionGroupDetailViewModel?> GetArtistDetailByNameAsync(string artistName, CancellationToken ct = default);

    /// <summary>GET /collections/system-view-detail?groupField=&amp;groupValue=&amp;mediaType=&amp;artistName= — grouped detail for non-routed system views such as music albums/artists.</summary>
    Task<CollectionGroupDetailViewModel?> GetSystemViewGroupDetailAsync(string groupField, string groupValue, string? mediaType = null, string? artistName = null, CancellationToken ct = default);

    /// <summary>GET /collections/managed — all non-Universe collections for the managed collections surface.</summary>
    Task<List<ManagedCollectionViewModel>> GetManagedCollectionsAsync(Guid? profileId = null, CancellationToken ct = default);

    /// <summary>GET /collections/catalog — classified collection data for the Collections hub.</summary>
    Task<List<CollectionManagementCatalogViewModel>> GetCollectionCatalogAsync(Guid? profileId = null, CancellationToken ct = default);

    /// <summary>GET /collections/managed/counts — collection count grouped by type for stats bar.</summary>
    Task<CollectionManagementCatalogViewModel?> GetCollectionSummaryAsync(Guid collectionId, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>GET /collections/content-groups — Universe-type collections (albums, TV series, book series, movie series) for the Content Groups section.</summary>
    Task<List<ContentGroupViewModel>> GetContentGroupsAsync(CancellationToken ct = default);

    /// <summary>GET /collections/system-views?mediaType=&amp;groupField= — system-view collections resolved as grouped content groups where no routed collection detail exists.</summary>
    Task<List<ContentGroupViewModel>> GetSystemViewGroupsAsync(string? mediaType = null, string? groupField = null, CancellationToken ct = default);

    /// <summary>GET /collections/{id}/items?limit= — curated items for a collection.</summary>
    Task<List<CollectionItemViewModel>> GetCollectionItemsAsync(Guid collectionId, int limit = 20, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>GET /collections/media-lookup - search local visible media for collection membership.</summary>
    Task<List<CollectionMediaLookupItemViewModel>> LookupCollectionMediaAsync(string? query, Guid? collectionId = null, string? mediaTypes = null, int offset = 0, int limit = 24, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>POST /collections/{id}/items — add a work to a playlist.</summary>
    Task<bool> AddCollectionItemAsync(Guid collectionId, Guid workId, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>DELETE /collections/{id}/items/{itemId} — remove a work from a playlist.</summary>
    Task<bool> RemoveCollectionItemAsync(Guid collectionId, Guid itemId, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>PUT /collections/{id}/items/reorder - persist playlist item ordering.</summary>
    Task<bool> ReorderCollectionItemsAsync(Guid collectionId, IReadOnlyList<Guid> itemIds, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>PUT /collections/{id}/enabled — toggle collection enabled state.</summary>
    Task<bool> UpdateCollectionEnabledAsync(Guid collectionId, bool enabled, CancellationToken ct = default);

    /// <summary>PUT /collections/{id}/featured — toggle collection featured state.</summary>
    Task<bool> UpdateCollectionFeaturedAsync(Guid collectionId, bool featured, CancellationToken ct = default);

    /// <summary>POST /collections/preview — evaluate rules without saving.</summary>
    Task<CollectionPreviewResult?> PreviewCollectionRulesAsync(List<CollectionRulePredicateViewModel> rules, string matchMode, int limit = 20, CancellationToken ct = default);

    /// <summary>POST /collections — create a new collection.</summary>
    Task<Guid?> CreateCollectionAndReturnIdAsync(string name, string? description, string? iconName, string collectionType, List<CollectionRulePredicateViewModel> rules, string matchMode, string? sortField, string sortDirection, bool liveUpdating, string visibility, Guid? profileId = null, CancellationToken ct = default);

    Task<bool> CreateCollectionAsync(string name, string? description, string? iconName, string collectionType, List<CollectionRulePredicateViewModel> rules, string matchMode, string? sortField, string sortDirection, bool liveUpdating, string visibility, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>PUT /collections/{id} — update a collection.</summary>
    Task<bool> UpdateCollectionAsync(Guid collectionId, string? name, string? description, string? iconName, List<CollectionRulePredicateViewModel>? rules, string? matchMode, string? visibility, string? sortField, string? sortDirection, bool? liveUpdating, bool? isEnabled, bool? isFeatured, Guid? profileId = null, CancellationToken ct = default);

    /// <summary>POST /collections/{id}/square-artwork — upload custom square artwork for a collection.</summary>
    Task<bool> UploadCollectionSquareArtworkAsync(Guid collectionId, Stream fileStream, string fileName, Guid? profileId = null, CancellationToken ct = default);

}
