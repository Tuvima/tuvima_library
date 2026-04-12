using MediaEngine.Storage.Models;
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

    /// <summary>GET /library/works — flat list of works with canonical values (excludes staging).</summary>
    Task<List<WorkViewModel>> GetLibraryWorksAsync(CancellationToken ct = default);

    /// <summary>POST /ingestion/scan — dry-run scan of a directory path.</summary>
    Task<ScanResultViewModel?> TriggerScanAsync(string? rootPath = null, CancellationToken ct = default);

    /// <summary>
    /// POST /ingestion/library-scan — Great Inhale: reads library.xml sidecars in the
    /// Library Root and hydrates the database. XML always wins on conflict.
    /// Returns null on failure.
    /// </summary>
    Task<LibraryScanResultViewModel?> TriggerLibraryScanAsync(CancellationToken ct = default);

    /// <summary>POST /ingestion/reconcile — scan all assets and clean orphans.</summary>
    Task<ReconciliationResultDto?> TriggerReconciliationAsync(CancellationToken ct = default);

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

    /// <summary>GET /metadata/pass2/status — pending count and enabled state for the Pass 2 deferred enrichment queue.</summary>
    Task<Pass2StatusDto?> GetPass2StatusAsync(CancellationToken ct = default);

    /// <summary>POST /metadata/pass2/trigger — trigger immediate Pass 2 (Universe Lookup) processing.</summary>
    Task<Pass2TriggerResultDto?> TriggerPass2NowAsync(CancellationToken ct = default);

    // ── Retag Sweep (auto re-tag) ─────────────────────────────────────────────

    /// <summary>GET /maintenance/retag-sweep/state — returns the pending diff + current hashes.</summary>
    Task<RetagSweepStateDto?> GetRetagSweepStateAsync(CancellationToken ct = default);

    /// <summary>POST /maintenance/retag-sweep/apply — commits the staged pending diff.</summary>
    Task<bool> ApplyRetagSweepPendingAsync(CancellationToken ct = default);

    /// <summary>POST /maintenance/retag-sweep/run-now — wakes the sweep worker immediately.</summary>
    Task<bool> RunRetagSweepNowAsync(CancellationToken ct = default);

    /// <summary>POST /maintenance/retag-sweep/retry/{assetId} — re-queues a single terminal-failed asset.</summary>
    Task<bool> RetryRetagForAssetAsync(Guid assetId, CancellationToken ct = default);

    // ── Initial Sweep (side-by-side-with-Plex plan §M) ───────────────────────

    /// <summary>POST /maintenance/initial-sweep/run — fire-and-forget hash sweep.</summary>
    Task<bool> RunInitialSweepAsync(CancellationToken ct = default);

    // ── QID Label Resolution (/metadata/labels) ────────────────────────────────

    /// <summary>POST /metadata/labels/resolve — batch-resolve QIDs to display labels.</summary>
    Task<Dictionary<string, LabelResolveViewModel>> ResolveLabelsAsync(
        IEnumerable<string> qids, CancellationToken ct = default);

    // ── Conflicts (/metadata/conflicts) ──────────────────────────────────────

    /// <summary>GET /metadata/conflicts — canonical values with unresolved metadata conflicts.</summary>
    Task<List<ConflictViewModel>> GetConflictsAsync(CancellationToken ct = default);

    // ── Watch Folder (/ingestion/watch-folder) ─────────────────────────────────

    /// <summary>GET /ingestion/watch-folder — list files currently in the Watch Folder.</summary>
    Task<List<WatchFolderFileViewModel>> GetWatchFolderAsync(CancellationToken ct = default);

    /// <summary>POST /ingestion/rescan — trigger re-processing of Watch Folder files.</summary>
    Task<bool> TriggerRescanAsync(CancellationToken ct = default);

    // ── Settings (/settings) ──────────────────────────────────────────────────

    /// <summary>GET /settings/server-general — server name and regional settings.</summary>
    Task<ServerGeneralSettingsDto?> GetServerGeneralAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/server-general — save server name and regional settings.</summary>
    Task<bool> UpdateServerGeneralAsync(ServerGeneralSettingsDto settings, CancellationToken ct = default);

    /// <summary>GET /settings/folders — current Watch Folder + Library Folder paths.</summary>
    Task<FolderSettingsDto?> GetFolderSettingsAsync(CancellationToken ct = default);

    /// <summary>GET /settings/libraries — per-library config (source paths, ReadOnly, writeback override).</summary>
    Task<List<LibraryFolderDto>?> GetLibrariesAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/folders — save paths to manifest and hot-swap the FileSystemWatcher.</summary>
    Task<bool> UpdateFolderSettingsAsync(FolderSettingsDto settings, CancellationToken ct = default);

    /// <summary>POST /settings/test-path — probe a directory for existence, read, and write access.</summary>
    Task<PathTestResultDto?> TestPathAsync(string path, CancellationToken ct = default);

    /// <summary>POST /settings/browse-directory — list subdirectories or drive roots.</summary>
    Task<BrowseDirectoryResultDto?> BrowseDirectoryAsync(string? path, CancellationToken ct = default);

    /// <summary>GET /providers/catalogue — consolidated UI metadata for all configured providers.</summary>
    Task<IReadOnlyList<ProviderCatalogueDto>> GetProviderCatalogueAsync(CancellationToken ct = default);

    /// <summary>GET /settings/providers — enabled state and live reachability for all providers.</summary>
    Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/providers/{name} — toggle a provider's enabled state.</summary>
    Task<bool> UpdateProviderAsync(string name, bool enabled, CancellationToken ct = default);

    /// <summary>GET /settings/providers/health — health status for all tracked providers.</summary>
    Task<List<ProviderHealthDto>> GetProviderHealthAsync(CancellationToken ct = default);

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

    /// <summary>GET /activity/run/{runId} — all entries for a specific ingestion run.</summary>
    Task<List<ActivityEntryViewModel>> GetActivityByRunIdAsync(Guid runId, CancellationToken ct = default);

    /// <summary>GET /activity/by-types?types=...&amp;limit= — entries filtered by action type for Timeline view.</summary>
    Task<List<ActivityEntryViewModel>> GetActivityByTypesAsync(
        string[] actionTypes, int limit = 50, CancellationToken ct = default);

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

    // ── Pipelines (/settings/pipelines) ─────────────────────────────────────

    /// <summary>GET /settings/pipelines — pipeline configuration per media type.</summary>
    Task<PipelineConfiguration?> GetPipelinesAsync(CancellationToken ct = default);

    /// <summary>PUT /settings/pipelines — save pipeline configuration.</summary>
    Task<bool> SavePipelinesAsync(PipelineConfiguration pipelines, CancellationToken ct = default);

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

    // ── Media File Upload ──────────────────────────────────────────────────

    /// <summary>POST /ingestion/upload — upload a media file and route it to the correct watch subfolder.</summary>
    Task<bool> UploadMediaAsync(MultipartFormDataContent content, CancellationToken ct = default);

    // ── Cover Art Upload ───────────────────────────────────────────────────

    /// <summary>POST /metadata/{entityId}/cover — upload cover art for a media asset.</summary>
    Task<bool> UploadCoverAsync(Guid entityId, Stream fileStream, string fileName, CancellationToken ct = default);

    // ── Provider Icons ───────────────────────────────────────────────────────

    /// <summary>POST /settings/providers/{name}/icon — upload a provider icon.</summary>
    Task<bool> UploadProviderIconAsync(string name, Stream fileStream, string fileName, CancellationToken ct = default);

    /// <summary>Returns the URL path for a provider's icon, or null if none exists.</summary>
    string GetProviderIconUrl(string name);

    // ── Development Seed (/dev) ────────────────────────────────────────

    /// <summary>POST /dev/seed-library — create test EPUBs in the Watch Folder (dev only).</summary>
    Task<bool> SeedLibraryAsync(CancellationToken ct = default);

    // ── Progress & Journey (/progress) ─────────────────────────────────

    /// <summary>GET /progress/journey?userId={id}&amp;limit= — incomplete items with Work+Hub context.
    /// Pass hubId to filter server-side to assets belonging to a specific hub.</summary>
    Task<List<JourneyItemViewModel>> GetJourneyAsync(Guid? userId = null, int limit = 5, Guid? hubId = null, CancellationToken ct = default);

    /// <summary>GET /progress/{assetId} - current progress for an asset.</summary>
    Task<ProgressStateDto?> GetProgressAsync(Guid assetId, CancellationToken ct = default);
    /// <summary>PUT /progress/{assetId} — upsert progress for a media asset.</summary>
    Task<bool> SaveProgressAsync(Guid assetId, Guid? userId = null, double progressPct = 0,
        Dictionary<string, string>? extendedProperties = null, CancellationToken ct = default);

    // ── Persons by Hub (/persons/by-hub) ────────────────────────────────

    /// <summary>GET /persons?role={role}&amp;limit={limit} — list persons as PersonListItemDto (for registry view).</summary>
    Task<IReadOnlyList<PersonListItemDto>?> GetPersonsAsync(string? role = null, int limit = 200, CancellationToken ct = default);

    /// <summary>GET /persons?role={role}&amp;limit={limit} â€” list persons filtered by role.</summary>
    Task<List<PersonViewModel>> GetPersonsByRoleAsync(
        string role, int limit = 50, CancellationToken ct = default);

    /// <summary>GET /persons/by-hub/{hubId} — all persons linked to works in a hub.</summary>
    Task<List<PersonViewModel>> GetPersonsByHubAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>GET /persons/by-work/{workId} — all persons linked to a specific work.</summary>
    Task<List<PersonViewModel>> GetPersonsByWorkAsync(Guid workId, CancellationToken ct = default);

    /// <summary>GET /persons/role-counts — count of persons per role.</summary>
    Task<Dictionary<string, int>> GetPersonRoleCountsAsync(CancellationToken ct = default);

    /// <summary>GET /persons/presence?ids=... — media type counts per person.</summary>
    Task<Dictionary<string, Dictionary<string, int>>> GetPersonPresenceAsync(IEnumerable<Guid> personIds, CancellationToken ct = default);


    // ── Related hubs (/hubs/{id}/related) ────────────────────────────────────

    /// <summary>GET /hubs/{id}/related?limit= — related hubs by series/author/genre cascade.</summary>
    Task<RelatedHubsViewModel?> GetRelatedHubsAsync(Guid hubId, int limit = 20, CancellationToken ct = default);

    // ── Parent Hub hierarchy (/hubs/parents, /hubs/{id}/children, /hubs/{id}/parent) ──

    /// <summary>GET /hubs/parents — returns all Parent Hubs (franchise-level groupings).</summary>
    Task<List<HubViewModel>> GetParentHubsAsync(CancellationToken ct = default);

    /// <summary>GET /hubs/{id}/children — returns child Hubs of the given Parent Hub.</summary>
    Task<List<HubViewModel>> GetChildHubsAsync(Guid parentHubId, CancellationToken ct = default);

    /// <summary>GET /hubs/{id}/parent — returns the Parent Hub of the given Hub, if any.</summary>
    Task<HubViewModel?> GetParentHubAsync(Guid hubId, CancellationToken ct = default);

    // \u2500\u2500 Person detail (/persons/{id}) \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

    /// <summary>GET /persons/{id} \u2014 full person detail with social links and enrichment data.</summary>
    Task<PersonDetailViewModel?> GetPersonDetailAsync(Guid personId, CancellationToken ct = default);

    /// <summary>GET /persons/{id}/works \u2014 all hubs containing works by this person.</summary>
    Task<List<HubViewModel>> GetWorksByPersonAsync(Guid personId, CancellationToken ct = default);

    /// <summary>GET /persons/{id}/aliases — aliases and pseudonyms for a person.</summary>
    Task<PersonAliasesResponseDto?> GetPersonAliasesAsync(Guid personId, CancellationToken ct = default);

    // ── Universe health + character data (/universe, /vault/characters, /vault/persons) ──

    /// <summary>GET /universe/{qid}/health — health score for a fictional universe.</summary>
    Task<UniverseHealthDto?> GetUniverseHealthAsync(string qid, CancellationToken ct = default);

    /// <summary>GET /vault/universes/{universeQid}/characters — characters in a universe with default actor/portrait.</summary>
    Task<IReadOnlyList<UniverseCharacterDto>> GetUniverseCharactersAsync(string universeQid, CancellationToken ct = default);

    /// <summary>GET /vault/persons/{personId}/character-roles — character roles with portraits for a person.</summary>
    Task<IReadOnlyList<CharacterRoleDto>> GetPersonCharacterRolesAsync(Guid personId, CancellationToken ct = default);

    /// <summary>PUT /vault/characters/{fictionalEntityId}/portraits/{portraitId}/default — set the default portrait for a character.</summary>
    Task SetDefaultPortraitAsync(Guid fictionalEntityId, Guid portraitId, CancellationToken ct = default);

    /// <summary>GET /vault/assets/{entityId} — entity assets grouped by type.</summary>
    Task<IReadOnlyList<EntityAssetDto>> GetEntityAssetsAsync(string entityId, CancellationToken ct = default);

    // ── Timeline (/timeline) ────────────────────────────────────────────────

    /// <summary>GET /timeline/{entityId} — full event history for an entity, newest first.</summary>
    Task<List<EntityTimelineEventDto>?> GetEntityTimelineAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>GET /timeline/{entityId}/pipeline — current pipeline state (latest per stage).</summary>
    Task<List<EntityTimelineEventDto>?> GetPipelineStateAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>GET /timeline/{entityId}/event/{eventId}/changes — field-level changes for a specific event.</summary>
    Task<List<EntityFieldChangeDto>?> GetEventFieldChangesAsync(Guid entityId, Guid eventId, CancellationToken ct = default);

    /// <summary>POST /timeline/{entityId}/revert/{eventId} — revert a sync writeback event.</summary>
    Task<bool> RevertSyncWritebackAsync(Guid entityId, Guid eventId, CancellationToken ct = default);

    /// <summary>Re-matches an entity through the full pipeline.</summary>
    Task<bool> RematchEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>POST /vault/enrichment/universe/trigger — manually trigger Stage 3 universe enrichment.</summary>
    Task TriggerUniverseEnrichmentAsync(CancellationToken ct = default);
    // â”€â”€ EPUB Reader (/read, /reader) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>GET /read/{assetId}/metadata â€” book metadata.</summary>
    Task<EpubBookMetadataDto?> GetBookMetadataAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>GET /read/{assetId}/toc â€” table of contents.</summary>
    Task<List<EpubTocEntryDto>> GetTableOfContentsAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>GET /read/{assetId}/chapter/{index} â€” chapter HTML.</summary>
    Task<EpubChapterContentDto?> GetChapterContentAsync(Guid assetId, int chapterIndex, CancellationToken ct = default);

    /// <summary>GET /read/{assetId}/search?q={query} â€” full-text search.</summary>
    Task<List<EpubSearchHitDto>> SearchEpubAsync(Guid assetId, string query, CancellationToken ct = default);

    /// <summary>GET /read/resolve/{workId} â€” resolve Work ID to Asset ID.</summary>
    Task<Guid?> ResolveWorkToAssetAsync(Guid workId, CancellationToken ct = default);

    /// <summary>GET /reader/{assetId}/bookmarks â€” list bookmarks.</summary>
    Task<List<ReaderBookmarkDto>> GetBookmarksAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>POST /reader/{assetId}/bookmarks â€” create bookmark.</summary>
    Task<ReaderBookmarkDto?> CreateBookmarkAsync(Guid assetId, int chapterIndex, string? cfiPosition, string? label, CancellationToken ct = default);

    /// <summary>DELETE /reader/bookmarks/{id} â€” delete bookmark.</summary>
    Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken ct = default);

    /// <summary>GET /reader/{assetId}/highlights â€” list highlights.</summary>
    Task<List<ReaderHighlightDto>> GetHighlightsAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>POST /reader/{assetId}/highlights â€” create highlight.</summary>
    Task<ReaderHighlightDto?> CreateHighlightAsync(Guid assetId, int chapterIndex, int startOffset, int endOffset, string selectedText, string? color, string? noteText, CancellationToken ct = default);

    /// <summary>PUT /reader/highlights/{id} â€” update highlight colour/note.</summary>
    Task<bool> UpdateHighlightAsync(Guid highlightId, string? color, string? noteText, CancellationToken ct = default);

    /// <summary>DELETE /reader/highlights/{id} â€” delete highlight.</summary>
    Task<bool> DeleteHighlightAsync(Guid highlightId, CancellationToken ct = default);

    /// <summary>GET /reader/{assetId}/statistics â€” reading statistics.</summary>
    Task<ReaderStatisticsDto?> GetReadingStatisticsAsync(Guid assetId, CancellationToken ct = default);

    /// <summary>PUT /reader/{assetId}/statistics â€” update reading statistics.</summary>
    Task<bool> UpdateReadingStatisticsAsync(Guid assetId, ReaderStatisticsUpdateDto stats, CancellationToken ct = default);

    /// <summary>

    // â”€â”€ Fan-out metadata search (/metadata/search-all) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>POST /metadata/search-all â€” fan-out search across all eligible providers.</summary>
    Task<FanOutSearchResponseViewModel?> SearchMetadataFanOutAsync(
        string query, string? mediaType = null, string? providerId = null,
        int maxResultsPerProvider = 5, CancellationToken ct = default);


    // ── Search results cache (/metadata/{entityId}/search-cache) ────────

    /// <summary>GET /metadata/{entityId}/search-cache — cached fan-out search results (30-day TTL).</summary>
    Task<string?> GetSearchResultsCacheAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>PUT /metadata/{entityId}/search-cache — store fan-out search results.</summary>
    Task SaveSearchResultsCacheAsync(Guid entityId, string resultsJson, CancellationToken ct = default);
    // â”€â”€ Canonical values (/metadata/canonical/{entityId}) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>GET /metadata/canonical/{entityId} â€” get all canonical values with provenance.</summary>
    Task<List<CanonicalFieldViewModel>> GetCanonicalValuesAsync(
        Guid entityId, CancellationToken ct = default);

    // â”€â”€ Cover from URL (/metadata/{entityId}/cover-from-url) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>POST /metadata/{entityId}/cover-from-url â€” download cover from provider URL.</summary>
    Task<bool> ApplyCoverFromUrlAsync(
        Guid entityId, string imageUrl, CancellationToken ct = default);
    /// Most recent error message from the last failed API call.
    /// Useful for surfacing diagnostic details in the UI.
    /// </summary>
    string? LastError { get; }

    // ── Universe Graph (Chronicle Explorer) ─────────────────────────────────

    /// <summary>GET /universe/{qid}/graph — fetch the universe relationship graph with optional filters.</summary>
    Task<UniverseGraphResponse?> GetUniverseGraphAsync(
        string qid,
        int? timelineYear = null,
        string? types = null,
        string? center = null,
        int? depth = null,
        CancellationToken ct = default);

    /// <summary>GET /universe/{qid}/lore-delta — check which entities have changed on Wikidata since last enrichment.</summary>
    Task<IReadOnlyList<LoreDeltaResultDto>> CheckLoreDeltaAsync(
        string qid, CancellationToken ct = default);

    /// <summary>GET /universes — list all narrative roots (fictional universes).</summary>
    Task<IReadOnlyList<NarrativeRootDto>> GetUniversesAsync(CancellationToken ct = default);

    /// <summary>
    /// POST /universe/entity/{qid}/deep-enrich — triggers on-demand deep enrichment for an
    /// entity and its un-enriched neighbors. Used by Chronicle Explorer when a user clicks
    /// on an entity that hasn't been deep-enriched yet.
    /// </summary>
    Task<DeepEnrichResponse?> TriggerDeepEnrichAsync(string entityQid, int depth = 2, CancellationToken ct = default);

    /// <summary>GET /universe/{qid}/cast — characters with their real-world performers.</summary>
    Task<UniverseCastResponse?> GetUniverseCastAsync(string qid, CancellationToken ct = default);

    /// <summary>GET /universe/{qid}/adaptations — adaptation chain (based_on/derivative_work/inspired_by).</summary>
    Task<UniverseAdaptationsResponse?> GetUniverseAdaptationsAsync(string qid, CancellationToken ct = default);

    /// <summary>GET /universe/{qid}/paths?from=X&amp;to=Y — find shortest paths between two entities.</summary>
    Task<UniversePathsResponse?> FindPathsAsync(
        string qid, string fromQid, string toQid, int maxHops = 4, CancellationToken ct = default);

    /// <summary>GET /universe/{qid}/family-tree?character=X — family tree rooted at a character.</summary>
    Task<FamilyTreeResponse?> GetFamilyTreeAsync(
        string qid, string characterQid, int generations = 3, CancellationToken ct = default);

    // ── Registry (/registry) ────────────────────────────────────────────────

    /// <summary>GET /registry/items — paginated list of all ingested items.</summary>
    Task<RegistryPageResponse?> GetRegistryItemsAsync(
        int offset = 0, int limit = 50,
        string? search = null, string? type = null, string? status = null,
        double? minConfidence = null, string? matchSource = null,
        bool? duplicatesOnly = null, bool? missingUniverseOnly = null,
        string? sort = null, int? maxDays = null,
        CancellationToken ct = default);

    /// <summary>POST /registry/batch/approve — bulk-approve registry items.</summary>
    Task<BatchRegistryResponse?> BatchApproveRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default);

    /// <summary>POST /registry/batch/delete — bulk-delete registry items.</summary>
    Task<BatchRegistryResponse?> BatchDeleteRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/reject — reject a single registry item.</summary>
    Task<BatchRegistryResponse?> RejectRegistryItemAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>POST /registry/batch/reject — bulk-reject registry items.</summary>
    Task<BatchRegistryResponse?> BatchRejectRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default);

    /// <summary>GET /registry/items/{entityId}/detail — full detail for expanded row.</summary>
    Task<RegistryItemDetailViewModel?> GetRegistryItemDetailAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>GET /registry/items/{entityId}/history — processing history timeline.</summary>
    Task<List<RegistryItemHistoryDto>> GetItemHistoryAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/recover — recover a previously rejected item.</summary>
    Task<bool> RecoverRegistryItemAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/auto-register — auto-register an item using its top candidate.</summary>
    Task<BatchRegistryResponse?> AutoRegisterItemAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/provisional — mark an item as provisional with curator metadata.</summary>
    Task<bool> MarkProvisionalAsync(Guid entityId, ProvisionalMetadataRequestDto metadata, CancellationToken ct = default);

    /// <summary>GET /registry/counts — status counts for tab badges.</summary>
    Task<RegistryStatusCountsDto?> GetRegistryStatusCountsAsync(CancellationToken ct = default);

    /// <summary>GET /registry/state-counts — four-state counts with trigger breakdown.</summary>
    Task<RegistryFourStateCountsDto?> GetRegistryFourStateCountsAsync(
        Guid? batchId = null, CancellationToken ct = default);

    /// <summary>GET /registry/type-counts — per-media-type item counts.</summary>
    Task<Dictionary<string, int>> GetRegistryTypeCountsAsync(CancellationToken ct = default);

    /// <summary>GET /ingestion/batches — recent ingestion batches.</summary>
    Task<IReadOnlyList<IngestionBatchViewModel>> GetIngestionBatchesAsync(
        int limit = 20, CancellationToken ct = default);

    /// <summary>GET /ingestion/batches/{id} — single batch detail.</summary>
    Task<IngestionBatchViewModel?> GetIngestionBatchByIdAsync(
        Guid id, CancellationToken ct = default);

    /// <summary>GET /ingestion/batches/attention-count — items needing attention.</summary>
    Task<int> GetBatchAttentionCountAsync(CancellationToken ct = default);

    // ── Search (/search) ──────────────────────────────────────────────────

    /// <summary>GET /metadata/{qid}/aliases — fetch Wikidata aliases (alternative titles) for a QID.</summary>
    Task<AliasesResponseDto?> GetAliasesAsync(string qid, CancellationToken ct = default);

    /// <summary>POST /search/universe — search Wikidata for identity candidates, enriched with cover art.</summary>
    Task<SearchUniverseResponseDto?> SearchUniverseAsync(
        string query, string mediaType, int maxCandidates = 5,
        string? localAuthor = null, CancellationToken ct = default);

    /// <summary>POST /search/retail — search retail providers for cover art and basic metadata.</summary>
    Task<SearchRetailResponseDto?> SearchRetailAsync(
        string query, string mediaType, int maxCandidates = 5,
        string? localTitle = null, string? localAuthor = null, string? localYear = null,
        Dictionary<string, string>? fileHints = null,
        Dictionary<string, string>? searchFields = null,
        CancellationToken ct = default);

    /// <summary>Unified resolve search with retail + description scoring.</summary>
    Task<SearchResolveResponseDto?> SearchResolveAsync(
        string query, string mediaType, int maxCandidates,
        Dictionary<string, string>? fileHints, CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/apply-match — apply a match to a registry item.</summary>
    Task<ApplyMatchResponseDto?> ApplyRegistryMatchAsync(
        Guid entityId, ApplyMatchRequestDto request,
        CancellationToken ct = default);

    /// <summary>POST /registry/items/{entityId}/create-manual — create a manual metadata entry.</summary>
    Task<CreateManualResponseDto?> CreateManualEntryAsync(
        Guid entityId, CreateManualRequestDto request,
        CancellationToken ct = default);

    /// <summary>DELETE /registry/items/{entityId} — permanently remove a work and all its files.</summary>
    Task<bool> DeleteRegistryItemAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Submit a problem report on a media item.</summary>
    Task<SubmitReportResponseDto?> SubmitReportAsync(SubmitReportRequestDto request, CancellationToken ct = default);

    /// <summary>Get all problem reports for a specific entity.</summary>
    Task<List<ReportEntryDto>> GetReportsForEntityAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>Resolve a problem report.</summary>
    Task<bool> ResolveReportAsync(long activityId, CancellationToken ct = default);

    /// <summary>Dismiss a problem report.</summary>
    Task<bool> DismissReportAsync(long activityId, CancellationToken ct = default);

    // ── AI Hardware Profile (/ai/profile, /ai/benchmark) ────────────────────

    /// <summary>GET /ai/profile — returns the cached hardware profile and performance tier.</summary>
    Task<HardwareProfileDto?> GetAiProfileAsync(CancellationToken ct = default);

    /// <summary>POST /ai/benchmark — re-runs the hardware benchmark and returns the updated profile.</summary>
    Task<HardwareProfileDto?> RunBenchmarkAsync(CancellationToken ct = default);

    /// <summary>GET /ai/enrichment/progress — pending and completed AI enrichment counts.</summary>
    Task<EnrichmentProgressDto?> GetEnrichmentProgressAsync(CancellationToken ct = default);

    /// <summary>GET /ai/resources — live RAM, CPU pressure, and transcoding status.</summary>
    Task<ResourceSnapshotDto?> GetResourceSnapshotAsync(CancellationToken ct = default);

    // ── Managed Hubs (Vault Hubs tab) ────────────────────────────────────────

    /// <summary>GET /hubs/{hubId}/group-detail — full drill-down view of a content group (album, TV show, book series, movie series).</summary>
    Task<HubGroupDetailViewModel?> GetHubGroupDetailAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>GET /hubs/artist-group-detail?hub_ids=... — combined multi-hub detail for artist drill-down.</summary>
    Task<HubGroupDetailViewModel?> GetArtistGroupDetailAsync(IEnumerable<Guid> hubIds, CancellationToken ct = default);

    /// <summary>GET /hubs/artist-detail-by-name?artistName=X — artist drill-down by name (system-view mode).</summary>
    Task<HubGroupDetailViewModel?> GetArtistDetailByNameAsync(string artistName, CancellationToken ct = default);

    /// <summary>GET /hubs/system-view-detail?groupField=&amp;groupValue=&amp;mediaType= — generic system-view drill-down for any group field.</summary>
    Task<HubGroupDetailViewModel?> GetSystemViewGroupDetailAsync(string groupField, string groupValue, string? mediaType = null, CancellationToken ct = default);

    /// <summary>GET /hubs/managed — all non-Universe hubs for the Vault Hubs tab.</summary>
    Task<List<ManagedHubViewModel>> GetManagedHubsAsync(CancellationToken ct = default);

    /// <summary>GET /hubs/managed/counts — hub count grouped by type for stats bar.</summary>
    Task<Dictionary<string, int>> GetManagedHubCountsAsync(CancellationToken ct = default);

    /// <summary>GET /hubs/content-groups — Universe-type hubs (albums, TV series, book series, movie series) for the Content Groups section.</summary>
    Task<List<ContentGroupViewModel>> GetContentGroupsAsync(CancellationToken ct = default);

    /// <summary>GET /hubs/system-views?mediaType=&amp;groupField= — System view hubs resolved as grouped content groups (By Show, By Artist, By Album).</summary>
    Task<List<ContentGroupViewModel>> GetSystemViewGroupsAsync(string? mediaType = null, string? groupField = null, CancellationToken ct = default);

    /// <summary>GET /hubs/{id}/items?limit= — curated items for a hub.</summary>
    Task<List<HubItemViewModel>> GetHubItemsAsync(Guid hubId, int limit = 20, CancellationToken ct = default);

    /// <summary>PUT /hubs/{id}/enabled — toggle hub enabled state.</summary>
    Task<bool> UpdateHubEnabledAsync(Guid hubId, bool enabled, CancellationToken ct = default);

    /// <summary>PUT /hubs/{id}/featured — toggle hub featured state.</summary>
    Task<bool> UpdateHubFeaturedAsync(Guid hubId, bool featured, CancellationToken ct = default);

    /// <summary>POST /hubs/preview — evaluate rules without saving.</summary>
    Task<HubPreviewResult?> PreviewHubRulesAsync(List<HubRulePredicateViewModel> rules, string matchMode, int limit = 20, CancellationToken ct = default);

    /// <summary>POST /hubs — create a new hub.</summary>
    Task<bool> CreateHubAsync(string name, List<HubRulePredicateViewModel> rules, string matchMode, string? sortField, string sortDirection, bool liveUpdating, CancellationToken ct = default);

    /// <summary>PUT /hubs/{id} — update a hub.</summary>
    Task<bool> UpdateHubAsync(Guid hubId, string? name, List<HubRulePredicateViewModel>? rules, string? matchMode, bool? isEnabled, bool? isFeatured, CancellationToken ct = default);

    /// <summary>DELETE /hubs/{id} — soft delete.</summary>
    Task<bool> DeleteHubAsync(Guid hubId, CancellationToken ct = default);

    /// <summary>GET /hubs/resolve/{id} — evaluate hub rules and return items.</summary>
    Task<List<HubResolvedItemViewModel>> ResolveHubAsync(Guid hubId, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// GET /hubs/resolve/by-name?name=...&amp;limit=... — resolves a System hub by display name.
    /// Bypasses the registry visibility filter so in-flight items are included.
    /// Reads both asset-level and root-parent-Work-level canonical values (lineage-aware).
    /// </summary>
    Task<List<HubResolvedItemViewModel>> ResolveHubByNameAsync(string name, int? limit = null, CancellationToken ct = default);

    // ── Vault Preferences (/settings/ui/vault-preferences) ──────────────────

    /// <summary>GET /settings/ui/vault-preferences — vault display preferences (view modes, show unowned).</summary>
    Task<VaultPreferencesSettings?> GetVaultPreferencesAsync();

    /// <summary>PUT /settings/ui/vault-preferences — save vault display preferences.</summary>
    Task SaveVaultPreferencesAsync(VaultPreferencesSettings settings);

    // ── Vault Overview ──

    /// <summary>GET /vault/overview — aggregated operational health summary.</summary>
    Task<VaultOverviewViewModel?> GetVaultOverviewAsync(CancellationToken ct = default);

    /// <summary>POST /vault/batch-edit — apply batch field edits to multiple items.</summary>
    Task<VaultBatchEditResultViewModel?> BatchEditAsync(
        List<Guid> entityIds, Dictionary<string, string> fieldChanges, CancellationToken ct = default);

    // ── Universe Alignment ──

    /// <summary>GET /vault/universe-candidates — works with universe QIDs but no hub assignment.</summary>
    Task<List<UniverseCandidateViewModel>> GetUniverseCandidatesAsync(CancellationToken ct = default);

    /// <summary>POST /vault/universe-candidates/{workId}/accept — accept a universe assignment.</summary>
    Task<bool> AcceptUniverseCandidateAsync(Guid workId, string targetHubQid, CancellationToken ct = default);

    /// <summary>POST /vault/universe-candidates/{workId}/reject — reject a universe candidate.</summary>
    Task<bool> RejectUniverseCandidateAsync(Guid workId, CancellationToken ct = default);

    /// <summary>POST /vault/universe-candidates/batch-accept — batch accept universe assignments.</summary>
    Task<int> BatchAcceptUniverseCandidatesAsync(List<Guid> workIds, CancellationToken ct = default);

    /// <summary>GET /vault/universe-unlinked — works with QID but no universe properties.</summary>
    Task<List<UnlinkedWorkViewModel>> GetUniverseUnlinkedAsync(CancellationToken ct = default);

    /// <summary>POST /vault/universe-assign — manually assign a work to a hub.</summary>
    Task<bool> ManualUniverseAssignAsync(Guid workId, Guid hubId, CancellationToken ct = default);
}



