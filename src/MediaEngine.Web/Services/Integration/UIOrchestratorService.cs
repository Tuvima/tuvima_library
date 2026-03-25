using Microsoft.AspNetCore.SignalR.Client;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Scoped orchestrator: the single bridge between <see cref="IEngineApiClient"/>,
/// the <see cref="UniverseStateContainer"/>, and the Engine API Intercom SignalR hub.
///
/// <para>
/// <b>Lifecycle:</b> one instance per Blazor Server circuit.  Components call
/// <see cref="StartSignalRAsync"/> during their first <c>OnInitializedAsync</c>
/// to activate the real-time channel for that circuit.
/// </para>
///
/// <para>
/// <b>SignalR events handled:</b>
/// <list type="bullet">
///   <item><c>"MediaAdded"</c> — invalidates the hub cache; next navigation triggers a fresh load.</item>
///   <item><c>"IngestionProgress"</c> — updates progress state in the container for live UI feedback.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Non-fatal connection failure:</b> if the API is offline when
/// <see cref="StartSignalRAsync"/> is called, the connection attempt is swallowed
/// and logged at Warning level.  The UI degrades gracefully to HTTP-only mode.
/// </para>
/// </summary>
public sealed class UIOrchestratorService : IAsyncDisposable
{
    private readonly IEngineApiClient              _api;
    private readonly UniverseStateContainer         _state;
    private readonly IConfiguration                 _config;
    private readonly ILogger<UIOrchestratorService> _logger;

    private HubConnection? _hubConnection;

    public UIOrchestratorService(
        IEngineApiClient              api,
        UniverseStateContainer         state,
        IConfiguration                 config,
        ILogger<UIOrchestratorService> logger)
    {
        _api    = api;
        _state  = state;
        _config = config;
        _logger = logger;
    }

    // ── Hubs ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the hub list, using the state container cache when available.
    /// Pass <paramref name="forceRefresh"/> = <see langword="true"/> to bypass the cache.
    /// </summary>
    public async Task<List<HubViewModel>> GetHubsAsync(
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (_state.IsLoaded && !forceRefresh)
            return [.. _state.Hubs];

        var hubs = await _api.GetHubsAsync(ct);
        _state.SetHubs(hubs);   // also rebuilds UniverseViewModel via UniverseMapper
        return hubs;
    }

    /// <summary>
    /// Returns the flattened <see cref="UniverseViewModel"/>, loading hub data
    /// from the API if not already cached.
    /// </summary>
    public async Task<UniverseViewModel> GetUniverseAsync(
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        // GetHubsAsync populates _state.Universe via UniverseMapper inside SetHubs.
        await GetHubsAsync(forceRefresh, ct);
        return _state.Universe ?? UniverseMapper.MapFromHubs([]);
    }

    // ── Library works ────────────────────────────────────────────────────────

    public Task<List<WorkViewModel>> GetLibraryWorksAsync(CancellationToken ct = default)
        => _api.GetLibraryWorksAsync(ct);

    // ── System status ─────────────────────────────────────────────────────────

    public async Task<SystemStatusViewModel?> GetSystemStatusAsync(CancellationToken ct = default)
    {
        var status = await _api.GetSystemStatusAsync(ct);
        if (status is not null)
            _state.Language = status.Language;
        return status;
    }

    // ── Ingestion ─────────────────────────────────────────────────────────────

    /// <summary>Triggers a dry-run scan and invalidates the hub cache on success.</summary>
    public async Task<ScanResultViewModel?> ScanAndRefreshAsync(
        string? rootPath = null,
        CancellationToken ct = default)
    {
        var result = await _api.TriggerScanAsync(rootPath, ct);
        if (result is not null)
            _state.Invalidate();
        return result;
    }

    // ── Metadata ──────────────────────────────────────────────────────────────

    /// <summary>Resolves a metadata conflict and invalidates the hub cache so the UI reflects it.</summary>
    public async Task<bool> ResolveMetadataAsync(
        Guid entityId, string claimKey, string chosenValue,
        CancellationToken ct = default)
    {
        var ok = await _api.ResolveMetadataAsync(entityId, claimKey, chosenValue, ct);
        if (ok)
            _state.Invalidate();
        return ok;
    }

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches works across all hubs.  Returns an empty list on failure or when
    /// the query is shorter than 2 characters (enforced server-side too).
    /// </summary>
    public Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default)
        => _api.SearchWorksAsync(query, ct);

    // ── API Key Management ────────────────────────────────────────────────────

    /// <summary>Lists all issued Guest API Keys (id, label, created_at only).</summary>
    public Task<List<ApiKeyViewModel>> GetApiKeysAsync(CancellationToken ct = default)
        => _api.GetApiKeysAsync(ct);

    /// <summary>Generates a new Guest API Key. The returned plaintext is shown exactly once.</summary>
    public Task<NewApiKeyViewModel?> CreateApiKeyAsync(string label, CancellationToken ct = default)
        => _api.CreateApiKeyAsync(label, ct);

    /// <summary>Revokes a Guest API Key. Any session using the key receives 401 immediately.</summary>
    public Task<bool> RevokeApiKeyAsync(Guid id, CancellationToken ct = default)
        => _api.RevokeApiKeyAsync(id, ct);

    /// <summary>Revokes all Guest API Keys in a single batch call. Returns count of revoked keys.</summary>
    public Task<int> RevokeAllApiKeysAsync(CancellationToken ct = default)
        => _api.RevokeAllApiKeysAsync(ct);

    // ── Profile Management ──────────────────────────────────────────────────────

    /// <summary>Lists all user profiles.</summary>
    public Task<List<ProfileViewModel>> GetProfilesAsync(CancellationToken ct = default)
        => _api.GetProfilesAsync(ct);

    /// <summary>
    /// Returns the currently active profile (first profile — the seed Owner by default).
    /// Ready for future session-based profile selection.
    /// </summary>
    public async Task<ProfileViewModel?> GetActiveProfileAsync(CancellationToken ct = default)
    {
        var profiles = await GetProfilesAsync(ct);
        return profiles?.FirstOrDefault();
    }

    /// <summary>Creates a new user profile. Returns true on success.</summary>
    public async Task<bool> CreateProfileAsync(
        string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default)
    {
        var result = await _api.CreateProfileAsync(displayName, avatarColor, role, navigationConfig, ct);
        return result is not null;
    }

    /// <summary>Updates an existing user profile and notifies the layout to refresh.</summary>
    public async Task<bool> UpdateProfileAsync(
        Guid id, string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default)
    {
        var ok = await _api.UpdateProfileAsync(id, displayName, avatarColor, role, navigationConfig, ct);
        if (ok)
            OnProfileChanged?.Invoke();
        return ok;
    }

    /// <summary>Deletes a user profile. Cannot delete the seed Owner profile or the last Administrator.</summary>
    public Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default)
        => _api.DeleteProfileAsync(id, ct);

    // ── Metadata Claims ─────────────────────────────────────────────────────────

    /// <summary>Returns claim history for a given entity (Work or Edition).</summary>
    public Task<List<ClaimHistoryDto>> GetClaimHistoryAsync(
        Guid entityId, CancellationToken ct = default)
        => _api.GetClaimHistoryAsync(entityId, ct);

    /// <summary>Creates a user-locked claim and invalidates the hub cache.</summary>
    public async Task<bool> LockClaimAsync(
        Guid entityId, string key, string value,
        CancellationToken ct = default)
    {
        var ok = await _api.LockClaimAsync(entityId, key, value, ct);
        if (ok)
            _state.Invalidate();
        return ok;
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    /// <summary>Returns the server name and regional settings.</summary>
    public Task<ServerGeneralSettingsDto?> GetServerGeneralAsync(CancellationToken ct = default)
        => _api.GetServerGeneralAsync(ct);

    /// <summary>Saves server name and regional settings.</summary>
    public Task<bool> UpdateServerGeneralAsync(ServerGeneralSettingsDto settings, CancellationToken ct = default)
        => _api.UpdateServerGeneralAsync(settings, ct);

    /// <summary>Returns the current Watch Folder and Library Folder configuration.</summary>
    public Task<FolderSettingsDto?> GetFolderSettingsAsync(CancellationToken ct = default)
        => _api.GetFolderSettingsAsync(ct);

    /// <summary>Saves updated folder paths to the Engine manifest and hot-swaps the file watcher.</summary>
    public Task<bool> UpdateFolderSettingsAsync(FolderSettingsDto settings, CancellationToken ct = default)
        => _api.UpdateFolderSettingsAsync(settings, ct);

    /// <summary>Probes a directory path for existence, read, and write access.</summary>
    public Task<PathTestResultDto?> TestPathAsync(string path, CancellationToken ct = default)
        => _api.TestPathAsync(path, ct);

    /// <summary>Lists subdirectories at the given path, or drive roots when the path is empty.</summary>
    public Task<BrowseDirectoryResultDto?> BrowseDirectoryAsync(string? path, CancellationToken ct = default)
        => _api.BrowseDirectoryAsync(path, ct);

    /// <summary>Returns enabled state and live reachability for all registered metadata providers.</summary>
    public Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default)
        => _api.GetProviderStatusAsync(ct);

    /// <summary>Toggles a provider's enabled state in the Engine manifest.</summary>
    public Task<bool> UpdateProviderAsync(string name, bool enabled, CancellationToken ct = default)
        => _api.UpdateProviderAsync(name, enabled, ct);

    /// <summary>Tests a provider's connectivity by sending a real request.</summary>
    public Task<ProviderTestResultDto?> TestProviderAsync(string name, CancellationToken ct = default)
        => _api.TestProviderAsync(name, ct);

    /// <summary>Fetches sample claims from a provider for the property picker.</summary>
    public Task<ProviderSampleResultDto?> FetchProviderSampleAsync(
        string name, string title, string? author = null,
        string? isbn = null, string? asin = null, string? mediaType = null,
        CancellationToken ct = default)
        => _api.FetchProviderSampleAsync(name, title, author, isbn, asin, mediaType, ct);

    /// <summary>Saves a provider's full configuration.</summary>
    public Task<bool> SaveProviderConfigAsync(string name, ProviderConfigUpdateDto config, CancellationToken ct = default)
        => _api.SaveProviderConfigAsync(name, config, ct);

    /// <summary>Disables/deletes a provider.</summary>
    public Task<bool> DeleteProviderAsync(string name, CancellationToken ct = default)
        => _api.DeleteProviderAsync(name, ct);

    /// <summary>Saves the provider priority order.</summary>
    public Task<bool> UpdateProviderPriorityAsync(List<string> order, CancellationToken ct = default)
        => _api.UpdateProviderPriorityAsync(order, ct);

    /// <summary>Most recent error detail from the last failed API call.</summary>
    public string? LastApiError => _api.LastError;

    // ── Activity Log (local in-memory) ──────────────────────────────────────

    /// <summary>Returns the current activity log (most recent first).</summary>
    public IReadOnlyList<ActivityEntry> GetActivityLog() => _state.ActivityLog;

    // ── System Activity (persistent Engine ledger) ───────────────────────────

    /// <summary>Returns the most recent system activity entries from the Engine's ledger.</summary>
    public Task<List<ActivityEntryViewModel>> GetRecentActivityAsync(
        int limit = 50, CancellationToken ct = default)
        => _api.GetRecentActivityAsync(limit, ct);

    /// <summary>Returns total entries and retention setting from the Engine's activity ledger.</summary>
    public Task<ActivityStatsViewModel?> GetActivityStatsAsync(CancellationToken ct = default)
        => _api.GetActivityStatsAsync(ct);

    /// <summary>Triggers a manual prune of activity entries older than the retention period.</summary>
    public Task<PruneResultViewModel?> TriggerPruneAsync(CancellationToken ct = default)
        => _api.TriggerPruneAsync(ct);

    /// <summary>Updates the activity retention period in days.</summary>
    public Task<bool> UpdateRetentionAsync(int days, CancellationToken ct = default)
        => _api.UpdateRetentionAsync(days, ct);

    /// <summary>Returns activity entries filtered by action types — used by Timeline view.</summary>
    public Task<List<ActivityEntryViewModel>> GetActivityByTypesAsync(
        string[] actionTypes, int limit = 50, CancellationToken ct = default)
        => _api.GetActivityByTypesAsync(actionTypes, limit, ct);

    /// <summary>Triggers a library reconciliation scan for missing files.</summary>
    public Task<ReconciliationResultDto?> TriggerReconciliationAsync(CancellationToken ct = default)
        => _api.TriggerReconciliationAsync(ct);

    /// <summary>Fires when the activity log changes. Components should use InvokeAsync(StateHasChanged).</summary>
    public event Action? OnActivityChanged
    {
        add    => _state.OnStateChanged += value;
        remove => _state.OnStateChanged -= value;
    }

    /// <summary>
    /// Fires when the Engine reports a folder health change via SignalR.
    /// Parameters: (path, isHealthy).
    /// Components should call <c>InvokeAsync(StateHasChanged)</c> in their handler.
    /// </summary>
    public event Action<string, bool>? OnFolderHealthChanged;

    // ── Watch Folder ─────────────────────────────────────────────────────────

    /// <summary>Returns files currently sitting in the Watch Folder.</summary>
    public Task<List<WatchFolderFileViewModel>> GetWatchFolderAsync(CancellationToken ct = default)
        => _api.GetWatchFolderAsync(ct);

    /// <summary>Triggers a re-scan of the Watch Folder, feeding all files into the pipeline.</summary>
    public Task<bool> TriggerRescanAsync(CancellationToken ct = default)
        => _api.TriggerRescanAsync(ct);

    // ── Hydration ──────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers Wikidata SPARQL deep hydration for a given entity.
    /// Returns the hydration result, or null on failure.
    /// </summary>
    public async Task<HydrateResultViewModel?> TriggerHydrationAsync(
        Guid entityId, CancellationToken ct = default)
    {
        var result = await _api.TriggerHydrationAsync(entityId, ct);
        if (result is { Success: true })
            _state.Invalidate();
        return result;
    }

    // ── Review Queue ─────────────────────────────────────────────────────────

    private int _reviewCount;

    /// <summary>Returns the cached pending review count. Call <see cref="RefreshReviewCountAsync"/> first.</summary>
    public int ReviewCount => _reviewCount;

    /// <summary>Fetches the pending review count from the Engine and caches it.</summary>
    public async Task<int> RefreshReviewCountAsync(CancellationToken ct = default)
    {
        _reviewCount = await _api.GetReviewCountAsync(ct);
        return _reviewCount;
    }

    /// <summary>Returns pending review queue items.</summary>
    public Task<List<ReviewItemViewModel>> GetPendingReviewsAsync(
        int limit = 50, CancellationToken ct = default)
        => _api.GetPendingReviewsAsync(limit, ct);

    /// <summary>Returns a single review item.</summary>
    public Task<ReviewItemViewModel?> GetReviewItemAsync(
        Guid id, CancellationToken ct = default)
        => _api.GetReviewItemAsync(id, ct);

    /// <summary>Resolves a review item and invalidates the hub cache.</summary>
    public async Task<bool> ResolveReviewAsync(
        Guid id, ReviewResolveRequestDto request, CancellationToken ct = default)
    {
        var ok = await _api.ResolveReviewItemAsync(id, request, ct);
        if (ok)
        {
            _reviewCount = Math.Max(0, _reviewCount - 1);
            _state.Invalidate();
        }
        return ok;
    }

    /// <summary>Permanently removes a work and all its files from the library.</summary>
    public async Task<bool> DeleteRegistryItemAsync(Guid entityId, CancellationToken ct = default)
    {
        return await _api.DeleteRegistryItemAsync(entityId, ct);
    }

    /// <summary>Dismisses a review item.</summary>
    public async Task<bool> DismissReviewAsync(Guid id, CancellationToken ct = default)
    {
        var ok = await _api.DismissReviewItemAsync(id, ct);
        if (ok)
            _reviewCount = Math.Max(0, _reviewCount - 1);
        return ok;
    }

    /// <summary>Skips Universe matching for a review item and dismisses it.</summary>
    public async Task<bool> SkipUniverseAsync(Guid id, CancellationToken ct = default)
    {
        var ok = await _api.SkipUniverseAsync(id, ct);
        if (ok)
        {
            _reviewCount = Math.Max(0, _reviewCount - 1);
            _state.Invalidate();
        }
        return ok;
    }

    /// <summary>Reclassifies a media asset to a different media type.</summary>
    public async Task<bool> ReclassifyMediaTypeAsync(
        Guid entityId, string mediaType, CancellationToken ct = default)
    {
        var ok = await _api.ReclassifyMediaTypeAsync(entityId, mediaType, ct);
        if (ok)
        {
            _reviewCount = Math.Max(0, _reviewCount - 1);
            _state.Invalidate();
        }
        return ok;
    }

    /// <summary>Fires when the review count changes (from SignalR events or explicit actions).</summary>
    public event Action? OnReviewCountChanged;

    /// <summary>Fires when the active profile is updated (display name or avatar colour).</summary>
    public event Action? OnProfileChanged;

    // ── Metadata Override ────────────────────────────────────────────────

    /// <summary>Overrides metadata fields for an entity and invalidates the hub cache.</summary>
    public async Task<bool> OverrideMetadataAsync(
        Guid entityId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        var ok = await _api.OverrideMetadataAsync(entityId, fields, ct);
        if (ok)
            _state.Invalidate();
        return ok;
    }

    // ── Hydration Settings ────────────────────────────────────────────────

    /// <summary>Returns hydration pipeline settings.</summary>
    public Task<HydrationSettingsDto?> GetHydrationSettingsAsync(CancellationToken ct = default)
        => _api.GetHydrationSettingsAsync(ct);

    /// <summary>Saves hydration pipeline settings.</summary>
    public Task<bool> UpdateHydrationSettingsAsync(
        HydrationSettingsDto settings, CancellationToken ct = default)
        => _api.UpdateHydrationSettingsAsync(settings, ct);

    // ── Pass 2 (Universe Lookup) ──────────────────────────────────────────

    /// <summary>Returns the pending count and enabled state for the Pass 2 deferred enrichment queue.</summary>
    public Task<Pass2StatusDto?> GetPass2StatusAsync(CancellationToken ct = default)
        => _api.GetPass2StatusAsync(ct);

    /// <summary>Triggers immediate Pass 2 (Universe Lookup) processing for all pending items.</summary>
    public Task<Pass2TriggerResultDto?> TriggerPass2NowAsync(CancellationToken ct = default)
        => _api.TriggerPass2NowAsync(ct);

    // ── Provider slots ──────────────────────────────────────────────────────

    /// <summary>Returns provider slot assignments per media type.</summary>
    public Task<Dictionary<string, ProviderSlotDto>?> GetProviderSlotsAsync(CancellationToken ct = default)
        => _api.GetProviderSlotsAsync(ct);

    /// <summary>Saves provider slot assignments. Returns true on success.</summary>
    public Task<bool> UpdateProviderSlotsAsync(
        Dictionary<string, ProviderSlotDto> slots, CancellationToken ct = default)
        => _api.UpdateProviderSlotsAsync(slots, ct);

    // ── Media types ─────────────────────────────────────────────────────────

    /// <summary>Returns media type definitions from the Engine.</summary>
    public Task<MediaTypeConfigurationDto?> GetMediaTypesAsync(CancellationToken ct = default)
        => _api.GetMediaTypesAsync(ct);

    /// <summary>Saves all media type definitions. Returns true on success.</summary>
    public Task<bool> SaveMediaTypesAsync(
        MediaTypeConfigurationDto config, CancellationToken ct = default)
        => _api.SaveMediaTypesAsync(config, ct);

    /// <summary>Adds a single custom media type. Returns updated config on success.</summary>
    public Task<MediaTypeConfigurationDto?> AddMediaTypeAsync(
        MediaTypeDefinitionDto newType, CancellationToken ct = default)
        => _api.AddMediaTypeAsync(newType, ct);

    /// <summary>Deletes a custom media type by key. Returns true on success.</summary>
    public Task<bool> DeleteMediaTypeAsync(string key, CancellationToken ct = default)
        => _api.DeleteMediaTypeAsync(key, ct);

    // ── Cover Art Upload ─────────────────────────────────────────────────

    /// <summary>Uploads cover art for a media asset.</summary>
    public Task<bool> UploadCoverAsync(Guid entityId, Stream fileStream, string fileName, CancellationToken ct = default)
        => _api.UploadCoverAsync(entityId, fileStream, fileName, ct);

    // ── Provider Icons ─────────────────────────────────────────────────────

    /// <summary>Uploads a custom icon for a provider.</summary>
    public Task<bool> UploadProviderIconAsync(string name, Stream fileStream, string fileName, CancellationToken ct = default)
        => _api.UploadProviderIconAsync(name, fileStream, fileName, ct);

    /// <summary>Returns the Engine URL path for a provider's icon.</summary>
    public string GetProviderIconUrl(string name) => _api.GetProviderIconUrl(name);

    // ── Metadata search ─────────────────────────────────────────────────────

    /// <summary>Searches a specific provider for metadata results.</summary>
    public Task<List<MetadataSearchResultDto>> SearchMetadataAsync(
        string providerName, string query, string? mediaType = null,
        int limit = 25, CancellationToken ct = default)
        => _api.SearchMetadataAsync(providerName, query, mediaType, limit, ct);

    // ── Development Seed ────────────────────────────────────────────────

    /// <summary>Seeds the library with test EPUBs (dev only).</summary>
    public Task<bool> SeedLibraryAsync(CancellationToken ct = default)
        => _api.SeedLibraryAsync(ct);

    // ── Progress & Journey ────────────────────────────────────────────────

    /// <summary>Returns incomplete journey items for the "Continue your Journey" hero.
    /// Pass hubId to get server-filtered results for a specific hub (no client-side matching needed).</summary>
    public Task<List<JourneyItemViewModel>> GetJourneyAsync(
        Guid? userId = null, int limit = 5, Guid? hubId = null, CancellationToken ct = default)
        => _api.GetJourneyAsync(userId, limit, hubId, ct);

    /// <summary>Retrieves the current progress state for a media asset.</summary>
    public Task<ProgressStateDto?> GetProgressAsync(
        Guid assetId, CancellationToken ct = default)
        => _api.GetProgressAsync(assetId, ct);

    /// <summary>Saves progress for a media asset.</summary>
    public Task<bool> SaveProgressAsync(
        Guid assetId, Guid? userId = null, double progressPct = 0,
        Dictionary<string, string>? extendedProperties = null,
        CancellationToken ct = default)
        => _api.SaveProgressAsync(assetId, userId, progressPct, extendedProperties, ct);

    /// <summary>Resolves a work ID to its primary media asset ID for playback.</summary>
    public Task<Guid?> ResolveWorkToAssetAsync(
        Guid workId, CancellationToken ct = default)
        => _api.ResolveWorkToAssetAsync(workId, ct);

    // ── Persons ──────────────────────────────────────────────────────────

    /// <summary>Returns persons as registry list items (for People filter in Registry Library view).</summary>
    public Task<IReadOnlyList<PersonListItemDto>?> GetPersonsAsync(
        string? role = null, int limit = 200, CancellationToken ct = default)
        => _api.GetPersonsAsync(role, limit, ct);

    /// <summary>Returns all persons linked to works in a hub.</summary>
    public Task<List<PersonViewModel>> GetPersonsByHubAsync(
        Guid hubId, CancellationToken ct = default)
        => _api.GetPersonsByHubAsync(hubId, ct);

    /// <summary>Returns all persons linked to a specific work.</summary>
    public Task<List<PersonViewModel>> GetPersonsByWorkAsync(
        Guid workId, CancellationToken ct = default)
        => _api.GetPersonsByWorkAsync(workId, ct);

    /// <summary>Returns persons filtered by role (e.g. "Author") for batch headshot loading.</summary>
    public Task<List<PersonViewModel>> GetPersonsByRoleAsync(
        string role, int limit = 50, CancellationToken ct = default)
        => _api.GetPersonsByRoleAsync(role, limit, ct);

    /// <summary>Returns related hubs (series/author/genre/explore cascade).</summary>
    public Task<RelatedHubsViewModel?> GetRelatedHubsAsync(
        Guid hubId, int limit = 20, CancellationToken ct = default)
        => _api.GetRelatedHubsAsync(hubId, limit, ct);

    /// <summary>Returns full person detail with social links.</summary>
    public Task<PersonDetailViewModel?> GetPersonDetailAsync(
        Guid personId, CancellationToken ct = default)
        => _api.GetPersonDetailAsync(personId, ct);

    /// <summary>Returns all hubs linked to works by a person.</summary>
    public Task<List<HubViewModel>> GetWorksByPersonAsync(
        Guid personId, CancellationToken ct = default)
        => _api.GetWorksByPersonAsync(personId, ct);

    // ── Parent Hub hierarchy ──────────────────────────────────────────────────

    /// <summary>Returns all Parent Hubs (franchise-level groupings).</summary>
    public Task<List<HubViewModel>> GetParentHubsAsync(CancellationToken ct = default)
        => _api.GetParentHubsAsync(ct);

    /// <summary>Returns child Hubs of the given Parent Hub.</summary>
    public Task<List<HubViewModel>> GetChildHubsAsync(
        Guid parentHubId, CancellationToken ct = default)
        => _api.GetChildHubsAsync(parentHubId, ct);

    /// <summary>Returns the Parent Hub of the given Hub, if any.</summary>
    public Task<HubViewModel?> GetParentHubAsync(
        Guid hubId, CancellationToken ct = default)
        => _api.GetParentHubAsync(hubId, ct);

    // ── Conflicts ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all canonical values that have unresolved metadata conflicts.
    /// Spec: Phase B – Conflict Surfacing (B-05).
    /// </summary>
    public Task<List<ConflictViewModel>> GetConflictsAsync(CancellationToken ct = default)
        => _api.GetConflictsAsync(ct);

    // ── Organization Template ─────────────────────────────────────────────────

    /// <summary>Gets the current file organization template and sample preview.</summary>
    public Task<OrganizationTemplateDto?> GetOrganizationTemplateAsync(CancellationToken ct = default)
        => _api.GetOrganizationTemplateAsync(ct);

    /// <summary>Saves a new file organization template. Returns the result with preview, or null on failure.</summary>
    public Task<OrganizationTemplateDto?> UpdateOrganizationTemplateAsync(string template, CancellationToken ct = default)
        => _api.UpdateOrganizationTemplateAsync(template, ct);

    // ── Registry ────────────────────────────────────────────────────────────

    /// <summary>Returns paginated registry items.</summary>
    public Task<RegistryPageResponse?> GetRegistryItemsAsync(
        int offset = 0, int limit = 50,
        string? search = null, string? type = null, string? status = null,
        double? minConfidence = null, string? matchSource = null,
        bool? duplicatesOnly = null, bool? missingUniverseOnly = null,
        string? sort = null, int? maxDays = null,
        CancellationToken ct = default)
        => _api.GetRegistryItemsAsync(offset, limit, search, type, status, minConfidence, matchSource, duplicatesOnly, missingUniverseOnly, sort, maxDays, ct);

    /// <summary>Bulk-approves registry items by entity ID.</summary>
    public Task<BatchRegistryResponse?> BatchApproveRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
        => _api.BatchApproveRegistryItemsAsync(entityIds, ct);

    /// <summary>Bulk-deletes registry items by entity ID.</summary>
    public Task<BatchRegistryResponse?> BatchDeleteRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
        => _api.BatchDeleteRegistryItemsAsync(entityIds, ct);

    /// <summary>Rejects a single registry item.</summary>
    public Task<BatchRegistryResponse?> RejectRegistryItemAsync(Guid entityId, CancellationToken ct = default)
        => _api.RejectRegistryItemAsync(entityId, ct);

    /// <summary>Bulk-rejects registry items by entity ID.</summary>
    public Task<BatchRegistryResponse?> BatchRejectRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
        => _api.BatchRejectRegistryItemsAsync(entityIds, ct);

    /// <summary>Returns full detail for a single registry item.</summary>
    public Task<RegistryItemDetailViewModel?> GetRegistryItemDetailAsync(Guid entityId, CancellationToken ct = default)
        => _api.GetRegistryItemDetailAsync(entityId, ct);

    /// <summary>Returns status counts for registry tab badges.</summary>
    public Task<RegistryStatusCountsDto?> GetRegistryStatusCountsAsync(CancellationToken ct = default)
        => _api.GetRegistryStatusCountsAsync(ct);

    /// <summary>Returns four-state counts (Registered, NeedsReview, NoMatch, Failed) with trigger breakdown.</summary>
    public Task<RegistryFourStateCountsDto?> GetRegistryFourStateCountsAsync(
        Guid? batchId = null, CancellationToken ct = default)
        => _api.GetRegistryFourStateCountsAsync(batchId, ct);

    /// <summary>Fetches recent ingestion batches from the Engine.</summary>
    public async Task<IReadOnlyList<IngestionBatchViewModel>> GetIngestionBatchesAsync(int limit = 20)
        => await _api.GetIngestionBatchesAsync(limit);

    /// <summary>Fetches a single ingestion batch by ID.</summary>
    public async Task<IngestionBatchViewModel?> GetIngestionBatchByIdAsync(Guid id)
        => await _api.GetIngestionBatchByIdAsync(id);

    /// <summary>Fetches the count of items needing curator attention across all batches.</summary>
    public async Task<int> GetBatchAttentionCountAsync()
        => await _api.GetBatchAttentionCountAsync();

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>POST /search/universe — Wikidata candidate search enriched with retail cover art.</summary>
    public async Task<List<UniverseCandidateDto>> SearchUniverseAsync(
        string query, string mediaType, int maxCandidates = 5,
        string? localAuthor = null, CancellationToken ct = default)
    {
        var result = await _api.SearchUniverseAsync(query, mediaType, maxCandidates, localAuthor, ct);
        return result?.Candidates ?? [];
    }

    /// <summary>POST /search/retail — retail provider candidate search with optional file hints for description scoring.</summary>
    public async Task<List<RetailCandidateDto>> SearchRetailAsync(
        string query, string mediaType, int maxCandidates = 5,
        string? localTitle = null, string? localAuthor = null, string? localYear = null,
        Dictionary<string, string>? fileHints = null,
        CancellationToken ct = default)
    {
        var result = await _api.SearchRetailAsync(
            query, mediaType, maxCandidates,
            localTitle, localAuthor, localYear,
            fileHints, ct);
        return result?.Candidates ?? [];
    }

    /// <summary>
    /// GET /metadata/{qid}/aliases — fetches Wikidata aliases (alternative titles) for the given QID.
    /// If <paramref name="canonicalTitle"/> is provided and is not already in the aliases list,
    /// it is prepended so the canonical title is always the first/default choice.
    /// Returns an empty list when the Engine returns no data or an error occurs.
    /// </summary>
    public async Task<List<string>> GetAliasesAsync(
        string qid, string? canonicalTitle = null, CancellationToken ct = default)
    {
        var response = await _api.GetAliasesAsync(qid, ct);
        if (response is null)
            return [];

        var aliases = response.Aliases ?? [];

        if (!string.IsNullOrWhiteSpace(canonicalTitle) &&
            !aliases.Contains(canonicalTitle, StringComparer.OrdinalIgnoreCase))
        {
            aliases = [canonicalTitle, .. aliases];
        }

        return aliases;
    }

    /// <summary>POST /registry/items/{entityId}/apply-match — apply a selected match.</summary>
    public async Task<ApplyMatchResponseDto?> ApplyRegistryMatchAsync(
        Guid entityId, ApplyMatchRequestDto request,
        CancellationToken ct = default)
    {
        var result = await _api.ApplyRegistryMatchAsync(entityId, request, ct);
        if (result is not null)
            _state.Invalidate(); // refresh hub list since metadata changed
        return result;
    }

    /// <summary>POST /registry/items/{entityId}/create-manual — create manual metadata entry.</summary>
    public Task<CreateManualResponseDto?> CreateManualEntryAsync(
        Guid entityId, CreateManualRequestDto request,
        CancellationToken ct = default)
        => _api.CreateManualEntryAsync(entityId, request, ct);

    /// <summary>Mark a registry item as provisional with curator-entered metadata.</summary>
    public Task<bool> MarkProvisionalAsync(Guid entityId, ProvisionalMetadataRequestDto metadata, CancellationToken ct = default)
        => _api.MarkProvisionalAsync(entityId, metadata, ct);

    // ── SignalR Intercom ───────────────────────────────────────────────────────

    /// <summary>
    /// Starts the SignalR connection to the Engine API Intercom hub at
    /// <c>{Engine:BaseUrl}/hubs/intercom</c>.
    ///
    /// <para>Idempotent — calling this multiple times is safe; the connection
    /// is only created and started once per circuit lifetime.</para>
    ///
    /// <para>Connection failure is non-fatal: the warning is logged and the
    /// UI continues in HTTP-only mode.</para>
    /// </summary>
    public async Task StartSignalRAsync(CancellationToken ct = default)
    {
        if (_hubConnection is not null)
            return; // Already initialised for this circuit.

        var baseUrl = _config["Engine:BaseUrl"] ?? "http://localhost:61495";
        var apiKey  = _config["Engine:ApiKey"]  ?? string.Empty;
        var hubUrl  = $"{baseUrl.TrimEnd('/')}/hubs/intercom";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                // Pass the API key as a request header so ApiKeyMiddleware
                // accepts the WebSocket upgrade request.
                if (!string.IsNullOrEmpty(apiKey))
                    options.Headers.Add("X-Api-Key", apiKey);
            })
            .WithAutomaticReconnect(new[]
            {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30),
            })
            .Build();

        // ── "MediaAdded" ──────────────────────────────────────────────────────
        // A new Work has been committed to the library.
        // Invalidate the cache so the grid refreshes on next render.
        _hubConnection.On<MediaAddedEvent>("MediaAdded", ev =>
        {
            _logger.LogInformation(
                "Intercom ← MediaAdded: WorkId={WorkId} Title=\"{Title}\" Type={MediaType}",
                ev.WorkId, ev.Title, ev.MediaType);
            _state.PushMediaAdded(ev);
        });

        // ── "IngestionCompleted" ────────────────────────────────────────────────
        // A file has been fully ingested (hashed, scored, and optionally moved).
        // The Engine publishes this event; invalidate the cache so the grid refreshes.
        _hubConnection.On<IngestionCompletedClientEvent>("IngestionCompleted", ev =>
        {
            _logger.LogInformation(
                "Intercom ← IngestionCompleted: File=\"{File}\" Type={MediaType}",
                ev.FilePath, ev.MediaType);
            _state.PushIngestionCompleted(ev);
        });

        // ── "IngestionProgress" ───────────────────────────────────────────────
        // Active ingestion tick — update the progress indicator.
        _hubConnection.On<IngestionProgressEvent>("IngestionProgress", ev =>
        {
            _logger.LogDebug(
                "Intercom ← IngestionProgress: [{Stage}] {Done}/{Total} — {File}",
                ev.Stage, ev.ProcessedCount, ev.TotalCount, ev.CurrentFile);
            _state.PushIngestionProgress(ev);
        });

        // ── "BatchProgress" ─────────────────────────────────────────────────
        // Per-file progress tick during an ingestion batch — carries running
        // counters and estimated time remaining for the active batch card.
        _hubConnection.On<BatchProgressEvent>("BatchProgress", ev =>
        {
            _logger.LogDebug(
                "Intercom ← BatchProgress: {Done}/{Total} ({Pct}%) ~{Eta}s remaining",
                ev.FilesProcessed, ev.FilesTotal, ev.ProgressPercent, ev.EstimatedSecondsRemaining);
            _state.PushBatchProgress(ev);
        });

        // ── "MetadataHarvested" ───────────────────────────────────────────────
        // An external provider updated cover art / description / narrator etc.
        // Invalidate the state cache so cards re-render with the new data.
        _hubConnection.On<MetadataHarvestedEvent>("MetadataHarvested", ev =>
        {
            _logger.LogDebug(
                "Intercom ← MetadataHarvested: EntityId={Id} Provider={Provider} Fields=[{Fields}]",
                ev.EntityId, ev.ProviderName, string.Join(",", ev.UpdatedFields));
            _state.PushMetadataHarvested(ev);
        });

        // ── "PersonEnriched" ──────────────────────────────────────────────────
        // Wikidata has enriched an author/narrator with a headshot + biography.
        _hubConnection.On<PersonEnrichedEvent>("PersonEnriched", ev =>
        {
            _logger.LogDebug(
                "Intercom ← PersonEnriched: PersonId={Id} Name={Name}",
                ev.PersonId, ev.Name);
            _state.PushPersonEnriched(ev);
        });

        // ── "MediaRemoved" ────────────────────────────────────────────────────
        // A file was removed (orphaned during ingestion scan or reconciliation).
        // Invalidate the hub cache so the home page refreshes on next render.
        _hubConnection.On("MediaRemoved", () =>
        {
            _logger.LogInformation("Intercom ← MediaRemoved: invalidating hub cache");
            _state.Invalidate();
        });

        // ── "WatchFolderActive" ───────────────────────────────────────────────
        // The Watch Folder has been updated; notify state container so interested
        // components (e.g. Settings page connection indicator) can react.
        _hubConnection.On<WatchFolderActiveEvent>("WatchFolderActive", ev =>
        {
            _logger.LogInformation(
                "Intercom ← WatchFolderActive: Dir={Dir} At={At}",
                ev.WatchDirectory, ev.ActivatedAt);
            _state.PushWatchFolderActive(ev);
        });

        // ── "ReviewItemCreated" ──────────────────────────────────────────────
        // A new review item was created by the hydration pipeline.
        _hubConnection.On<ReviewItemCreatedEvent>("ReviewItemCreated", ev =>
        {
            _logger.LogDebug(
                "Intercom ← ReviewItemCreated: ReviewId={Id} EntityId={EntityId} Trigger={Trigger}",
                ev.ReviewItemId, ev.EntityId, ev.Trigger);
            _reviewCount++;
            OnReviewCountChanged?.Invoke();
        });

        // ── "ReviewItemResolved" ─────────────────────────────────────────────
        // A review item was resolved or dismissed.
        _hubConnection.On<ReviewItemResolvedEvent>("ReviewItemResolved", ev =>
        {
            _logger.LogDebug(
                "Intercom ← ReviewItemResolved: ReviewId={Id} Status={Status}",
                ev.ReviewItemId, ev.Status);
            _reviewCount = Math.Max(0, _reviewCount - 1);
            _state.Invalidate();
            OnReviewCountChanged?.Invoke();
        });

        // ── "HydrationStageCompleted" ────────────────────────────────────────
        // A pipeline stage completed — metadata may have changed.
        _hubConnection.On<HydrationStageCompletedEvent>("HydrationStageCompleted", ev =>
        {
            _logger.LogDebug(
                "Intercom ← HydrationStageCompleted: EntityId={Id} Stage={Stage} Claims={Claims}",
                ev.EntityId, ev.Stage, ev.ClaimsAdded);
            _state.Invalidate();
        });

        // ── "FolderHealthChanged" ───────────────────────────────────────────
        // Periodic health check reports whether Watch/Library folders are accessible.
        // LibrariesTab subscribes to OnFolderHealthChanged to update status dots.
        _hubConnection.On<FolderHealthChangedEvent>("FolderHealthChanged", ev =>
        {
            _logger.LogDebug(
                "Intercom ← FolderHealthChanged: Path={Path} Accessible={Ok}",
                ev.Path, ev.IsAccessible);

            // Determine folder type by comparing with current known paths.
            var healthy = ev.IsAccessible && ev.HasRead && ev.HasWrite;
            OnFolderHealthChanged?.Invoke(ev.Path, healthy);
        });

        // ── Connection lifecycle logging ──────────────────────────────────────
        _hubConnection.Reconnecting += ex =>
        {
            _logger.LogWarning("Intercom reconnecting: {Message}", ex?.Message);
            return Task.CompletedTask;
        };
        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("Intercom reconnected (connectionId={Id})", connectionId);
            return Task.CompletedTask;
        };
        _hubConnection.Closed += ex =>
        {
            _logger.LogWarning("Intercom closed: {Message}", ex?.Message);
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync(ct);
            _logger.LogInformation("Intercom connected → {Url}", hubUrl);
            _state.PushServerStarted();
        }
        catch (Exception ex)
        {
            // Non-fatal: degrade gracefully to HTTP-only mode.
            _logger.LogWarning(ex,
                "Could not connect to Intercom hub at {Url} — real-time updates disabled.", hubUrl);
        }
    }

    /// <summary>
    /// Whether the SignalR connection is currently established.
    /// Useful for rendering a live-indicator badge in the app bar.
    /// </summary>
    public bool IsIntercomConnected =>
        _hubConnection?.State == HubConnectionState.Connected;

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    // â”€â”€ Fan-out search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public Task<FanOutSearchResponseViewModel?> SearchMetadataFanOutAsync(
        string query, string? mediaType = null, string? providerId = null,
        int maxResultsPerProvider = 5, CancellationToken ct = default)
        => _api.SearchMetadataFanOutAsync(query, mediaType, providerId, maxResultsPerProvider, ct);

    // ── Search results cache ────────────────────────────────────────────

    public Task<string?> GetSearchResultsCacheAsync(Guid entityId, CancellationToken ct = default)
        => _api.GetSearchResultsCacheAsync(entityId, ct);

    public Task SaveSearchResultsCacheAsync(Guid entityId, string resultsJson, CancellationToken ct = default)
        => _api.SaveSearchResultsCacheAsync(entityId, resultsJson, ct);


    // â”€â”€ Canonical values â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public Task<List<CanonicalFieldViewModel>> GetCanonicalValuesAsync(
        Guid entityId, CancellationToken ct = default)
        => _api.GetCanonicalValuesAsync(entityId, ct);

    // â”€â”€ Cover from URL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public Task<bool> ApplyCoverFromUrlAsync(
        Guid entityId, string imageUrl, CancellationToken ct = default)
        => _api.ApplyCoverFromUrlAsync(entityId, imageUrl, ct);

    public Task<SubmitReportResponseDto?> SubmitReportAsync(SubmitReportRequestDto request, CancellationToken ct = default)
        => _api.SubmitReportAsync(request, ct);

    public Task<List<ReportEntryDto>> GetReportsForEntityAsync(Guid entityId, CancellationToken ct = default)
        => _api.GetReportsForEntityAsync(entityId, ct);

    public Task<bool> ResolveReportAsync(long activityId, CancellationToken ct = default)
        => _api.ResolveReportAsync(activityId, ct);

    public Task<bool> DismissReportAsync(long activityId, CancellationToken ct = default)
        => _api.DismissReportAsync(activityId, ct);
}

