using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Strongly-typed HTTP client for the Engine API.
/// All methods are fire-and-forget safe: they return null / empty list on failure
/// rather than throwing, so callers control error display.
/// </summary>
public interface IEngineApiClient
{
    /// <summary>GET /system/status — lightweight connectivity probe.</summary>
    Task<SystemStatusViewModel?> GetSystemStatusAsync(CancellationToken ct = default);

    /// <summary>GET /hubs — full hub list with works and canonical values.</summary>
    Task<List<HubViewModel>> GetHubsAsync(CancellationToken ct = default);

    /// <summary>POST /ingestion/scan — dry-run scan of a directory path.</summary>
    Task<ScanResultViewModel?> TriggerScanAsync(string? rootPath = null, CancellationToken ct = default);

    /// <summary>
    /// POST /ingestion/library-scan — Great Inhale: reads library.xml sidecars in the
    /// Library Root and hydrates the database. XML always wins on conflict.
    /// Returns null on failure.
    /// </summary>
    Task<LibraryScanResultViewModel?> TriggerLibraryScanAsync(CancellationToken ct = default);

    /// <summary>PATCH /metadata/resolve — manually override a metadata canonical value.</summary>
    Task<bool> ResolveMetadataAsync(
        Guid   entityId,
        string claimKey,
        string chosenValue,
        CancellationToken ct = default);

    /// <summary>GET /hubs/search?q= — full-text search across all works (min 2 chars).</summary>
    Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default);

    // ── API key management (/admin/api-keys) ──────────────────────────────────

    /// <summary>GET /admin/api-keys — list all issued keys (id, label, created_at).</summary>
    Task<List<ApiKeyViewModel>> GetApiKeysAsync(CancellationToken ct = default);

    /// <summary>POST /admin/api-keys — generate a new key. Returns key + one-time plaintext.</summary>
    Task<NewApiKeyViewModel?> CreateApiKeyAsync(string label, CancellationToken ct = default);

    /// <summary>DELETE /admin/api-keys/{id} — revoke a key immediately.</summary>
    Task<bool> RevokeApiKeyAsync(Guid id, CancellationToken ct = default);

    /// <summary>DELETE /admin/api-keys — revoke all keys in a single batch. Returns count of revoked keys.</summary>
    Task<int> RevokeAllApiKeysAsync(CancellationToken ct = default);

    // ── Profiles (/profiles) ────────────────────────────────────────────────────

    /// <summary>GET /profiles — list all user profiles.</summary>
    Task<List<ProfileViewModel>> GetProfilesAsync(CancellationToken ct = default);

    /// <summary>POST /profiles — create a new user profile.</summary>
    Task<ProfileViewModel?> CreateProfileAsync(
        string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default);

    /// <summary>PUT /profiles/{id} — update an existing profile.</summary>
    Task<bool> UpdateProfileAsync(
        Guid id, string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default);

    /// <summary>DELETE /profiles/{id} — delete a profile.</summary>
    Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default);

    // ── Metadata claims (/metadata) ─────────────────────────────────────────────

    /// <summary>GET /metadata/claims/{entityId} — claim history for a work/edition.</summary>
    Task<List<ClaimHistoryDto>> GetClaimHistoryAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>PATCH /metadata/lock-claim — create a user-locked claim.</summary>
    Task<bool> LockClaimAsync(Guid entityId, string key, string value, CancellationToken ct = default);

    // ── Hydration (/metadata/hydrate) ──────────────────────────────────────────

    /// <summary>POST /metadata/hydrate/{entityId} — trigger Wikidata SPARQL deep hydration.</summary>
    Task<HydrateResultViewModel?> TriggerHydrationAsync(
        Guid entityId, CancellationToken ct = default);

    // ── Conflicts (/metadata/conflicts) ──────────────────────────────────────

    /// <summary>GET /metadata/conflicts — canonical values with unresolved metadata conflicts.</summary>
    Task<List<ConflictViewModel>> GetConflictsAsync(CancellationToken ct = default);

    // ── Watch Folder (/ingestion/watch-folder) ─────────────────────────────────

    /// <summary>GET /ingestion/watch-folder — list files currently in the Watch Folder.</summary>
    Task<List<WatchFolderFileViewModel>> GetWatchFolderAsync(CancellationToken ct = default);

    /// <summary>POST /ingestion/rescan — trigger re-processing of Watch Folder files.</summary>
    Task<bool> TriggerRescanAsync(CancellationToken ct = default);

    // ── Settings (/settings) ──────────────────────────────────────────────────

    /// <summary>GET /settings/folders — current Watch Folder + Library Folder paths.</summary>
    Task<FolderSettingsDto?> GetFolderSettingsAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/folders — save paths to manifest and hot-swap the FileSystemWatcher.</summary>
    Task<bool> UpdateFolderSettingsAsync(FolderSettingsDto settings, CancellationToken ct = default);

    /// <summary>POST /settings/test-path — probe a directory for existence, read, and write access.</summary>
    Task<PathTestResultDto?> TestPathAsync(string path, CancellationToken ct = default);

    /// <summary>GET /settings/providers — enabled state and live reachability for all providers.</summary>
    Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/providers/{name} — toggle a provider's enabled state.</summary>
    Task<bool> UpdateProviderAsync(string name, bool enabled, CancellationToken ct = default);

    /// <summary>POST /settings/providers/{name}/test — test a provider's connectivity.</summary>
    Task<ProviderTestResultDto?> TestProviderAsync(string name, CancellationToken ct = default);

    /// <summary>POST /settings/providers/{name}/sample — fetch sample claims from a provider.</summary>
    Task<ProviderSampleResultDto?> FetchProviderSampleAsync(
        string name, string title, string? author = null,
        string? isbn = null, string? asin = null, string? mediaType = null,
        CancellationToken ct = default);

    /// <summary>PUT /settings/providers/{name}/config — save full provider configuration.</summary>
    Task<bool> SaveProviderConfigAsync(string name, ProviderConfigUpdateDto config, CancellationToken ct = default);

    /// <summary>DELETE /settings/providers/{name} — disable/delete a provider.</summary>
    Task<bool> DeleteProviderAsync(string name, CancellationToken ct = default);

    /// <summary>PUT /settings/providers/priority — save provider priority order.</summary>
    Task<bool> UpdateProviderPriorityAsync(List<string> order, CancellationToken ct = default);

    // ── Organization template ─────────────────────────────────────────────────

    /// <summary>GET /settings/organization-template — current file organization template + preview.</summary>
    Task<OrganizationTemplateDto?> GetOrganizationTemplateAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/organization-template — save a new file organization template.</summary>
    Task<OrganizationTemplateDto?> UpdateOrganizationTemplateAsync(string template, CancellationToken ct = default);

    // ── Activity log (/activity) ────────────────────────────────────────────────

    /// <summary>GET /activity/recent?limit= — most recent system activity entries.</summary>
    Task<List<ActivityEntryViewModel>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>GET /activity/stats — total entries and retention setting.</summary>
    Task<ActivityStatsViewModel?> GetActivityStatsAsync(CancellationToken ct = default);

    /// <summary>POST /activity/prune — manually prune old activity entries.</summary>
    Task<PruneResultViewModel?> TriggerPruneAsync(CancellationToken ct = default);

    /// <summary>PUT /activity/retention?days= — update retention period.</summary>
    Task<bool> UpdateRetentionAsync(int days, CancellationToken ct = default);

    // ── UI Settings (/settings/ui) ───────────────────────────────────────────────

    /// <summary>
    /// GET /settings/ui/resolved?device={class}&amp;profile={id} — fully cascaded UI settings
    /// for the given device class and optional profile.
    /// </summary>
    Task<ResolvedUISettingsViewModel?> GetResolvedUISettingsAsync(
        string deviceClass = "web",
        string? profileId = null,
        CancellationToken ct = default);

    // ── Review queue (/review) ──────────────────────────────────────────────

    /// <summary>GET /review/pending?limit= — list pending review queue items.</summary>
    Task<List<ReviewItemViewModel>> GetPendingReviewsAsync(int limit = 50, CancellationToken ct = default);

    /// <summary>GET /review/{id} — single review item with full detail.</summary>
    Task<ReviewItemViewModel?> GetReviewItemAsync(Guid id, CancellationToken ct = default);

    /// <summary>GET /review/count — pending count for sidebar badge.</summary>
    Task<int> GetReviewCountAsync(CancellationToken ct = default);

    /// <summary>POST /review/{id}/resolve — resolve a review item.</summary>
    Task<bool> ResolveReviewItemAsync(Guid id, ReviewResolveRequestDto request, CancellationToken ct = default);

    /// <summary>POST /review/{id}/dismiss — dismiss a review item.</summary>
    Task<bool> DismissReviewItemAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /review/{id}/skip-universe — skip Universe matching and dismiss the item.</summary>
    Task<bool> SkipUniverseAsync(Guid id, CancellationToken ct = default);

    /// <summary>POST /metadata/{entityId}/reclassify — reclassify a media asset to a different media type.</summary>
    Task<bool> ReclassifyMediaTypeAsync(Guid entityId, string mediaType, CancellationToken ct = default);

    // ── Provider slots (/settings/provider-slots) ──────────────────────────

    /// <summary>GET /settings/provider-slots — current Primary/Secondary/Tertiary slots per media type.</summary>
    Task<Dictionary<string, ProviderSlotDto>?> GetProviderSlotsAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/provider-slots — save slot assignments for all media types.</summary>
    Task<bool> UpdateProviderSlotsAsync(Dictionary<string, ProviderSlotDto> slots, CancellationToken ct = default);

    // ── Metadata search (/metadata/search) ───────────────────────────────

    /// <summary>POST /metadata/search — multi-result metadata search against a specific provider.</summary>
    Task<List<MetadataSearchResultDto>> SearchMetadataAsync(
        string providerName, string query, string? mediaType = null,
        int limit = 25, CancellationToken ct = default);

    // ── Metadata override (/metadata/{entityId}/override) ─────────────────

    /// <summary>PUT /metadata/{entityId}/override — create user-locked claims for multiple fields.</summary>
    Task<bool> OverrideMetadataAsync(Guid entityId, Dictionary<string, string> fields, CancellationToken ct = default);

    // ── Media types (/settings/media-types) ────────────────────────────────

    /// <summary>GET /settings/media-types — load media type definitions.</summary>
    Task<MediaTypeConfigurationDto?> GetMediaTypesAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/media-types — save all media type definitions.</summary>
    Task<bool> SaveMediaTypesAsync(MediaTypeConfigurationDto config, CancellationToken ct = default);

    /// <summary>POST /settings/media-types/add — add a single custom media type.</summary>
    Task<MediaTypeConfigurationDto?> AddMediaTypeAsync(MediaTypeDefinitionDto newType, CancellationToken ct = default);

    /// <summary>DELETE /settings/media-types/{key} — delete a custom media type.</summary>
    Task<bool> DeleteMediaTypeAsync(string key, CancellationToken ct = default);

    // ── Hydration settings (/settings/hydration) ──────────────────────────

    /// <summary>GET /settings/hydration — load hydration pipeline configuration.</summary>
    Task<HydrationSettingsDto?> GetHydrationSettingsAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/hydration — save hydration pipeline configuration.</summary>
    Task<bool> UpdateHydrationSettingsAsync(HydrationSettingsDto settings, CancellationToken ct = default);

    // ── Provider Icons ───────────────────────────────────────────────────────

    /// <summary>POST /settings/providers/{name}/icon — upload a provider icon.</summary>
    Task<bool> UploadProviderIconAsync(string name, Stream fileStream, string fileName, CancellationToken ct = default);

    /// <summary>Returns the URL path for a provider's icon, or null if none exists.</summary>
    string GetProviderIconUrl(string name);

    /// <summary>
    /// Most recent error message from the last failed API call.
    /// Useful for surfacing diagnostic details in the UI.
    /// </summary>
    string? LastError { get; }
}
