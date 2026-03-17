using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Strongly-typed HTTP client for the Engine API.
/// Registered via <c>AddHttpClient&lt;EngineApiClient&gt;</c> in Program.cs so the
/// base address and X-Api-Key header are injected once at startup.
/// </summary>
public sealed class EngineApiClient : IEngineApiClient
{
    private readonly HttpClient                      _http;
    private readonly ILogger<EngineApiClient>        _logger;

    public EngineApiClient(HttpClient http, ILogger<EngineApiClient> logger)
    {
        _http   = http;
        _logger = logger;
    }

    // ── GET /system/status ────────────────────────────────────────────────────

    public async Task<SystemStatusViewModel?> GetSystemStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<StatusRaw>("/system/status", ct);
            return raw is null ? null : new SystemStatusViewModel
            {
                Status  = raw.Status,
                Version = raw.Version,
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            // Debug level: this endpoint is polled; Engine may not be up yet.
            _logger.LogDebug(ex, "GET /system/status failed");
            return null;
        }
    }

    // ── GET /hubs ─────────────────────────────────────────────────────────────

    public async Task<List<HubViewModel>> GetHubsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<HubRaw>>("/hubs", ct);
            return raw?.Select(MapHub).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs failed");
            return [];
        }
    }

    // ── POST /ingestion/scan ──────────────────────────────────────────────────

    public async Task<ScanResultViewModel?> TriggerScanAsync(
        string? rootPath = null,
        CancellationToken ct = default)
    {
        try
        {
            var body    = new { root_path = rootPath };
            var resp    = await _http.PostAsJsonAsync("/ingestion/scan", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var raw     = await resp.Content.ReadFromJsonAsync<ScanRaw>(ct);
            return raw is null ? null : new ScanResultViewModel
            {
                Operations = raw.Operations.Select(o => new PendingOperationViewModel
                {
                    SourcePath      = o.SourcePath,
                    DestinationPath = o.DestinationPath,
                    OperationKind   = o.OperationKind,
                    Reason          = o.Reason,
                }).ToList(),
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/scan failed");
            return null;
        }
    }

    // ── POST /ingestion/library-scan ─────────────────────────────────────────

    public async Task<LibraryScanResultViewModel?> TriggerLibraryScanAsync(
        CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/ingestion/library-scan", new { }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<LibraryScanResultViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/library-scan failed");
            return null;
        }
    }

    // ── POST /ingestion/reconcile ─────────────────────────────────────────────

    public async Task<ReconciliationResultDto?> TriggerReconciliationAsync(
        CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/ingestion/reconcile", new { }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ReconciliationResultDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/reconcile failed");
            return null;
        }
    }

    // ── GET /ingestion/watch-folder ────────────────────────────────────────────

    public async Task<List<WatchFolderFileViewModel>> GetWatchFolderAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<WatchFolderResponse>("/ingestion/watch-folder", ct);
            return raw?.Files ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /ingestion/watch-folder failed");
            return [];
        }
    }

    // ── POST /ingestion/rescan ──────────────────────────────────────────────

    public async Task<bool> TriggerRescanAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/ingestion/rescan", new { }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/rescan failed");
            return false;
        }
    }

    // ── PATCH /metadata/resolve ───────────────────────────────────────────────

    public async Task<bool> ResolveMetadataAsync(
        Guid entityId, string claimKey, string chosenValue, CancellationToken ct = default)
    {
        try
        {
            var body = new { entity_id = entityId, claim_key = claimKey, chosen_value = chosenValue };
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), "/metadata/resolve")
            {
                Content = JsonContent.Create(body),
            };
            var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PATCH /metadata/resolve failed");
            return false;
        }
    }

    // ── GET /hubs/search ─────────────────────────────────────────────────────

    public async Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(query);
            var raw = await _http.GetFromJsonAsync<List<SearchRawResult>>(
                $"/hubs/search?q={encoded}", ct);
            return raw?.Select(r => new SearchResultViewModel
            {
                WorkId         = r.WorkId,
                HubId          = r.HubId,
                Title          = r.Title,
                Author         = r.Author,
                MediaType      = r.MediaType,
                HubDisplayName = r.HubDisplayName,
            }).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/search failed");
            return [];
        }
    }

    // ── /admin/api-keys ───────────────────────────────────────────────────────

    public async Task<List<ApiKeyViewModel>> GetApiKeysAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ApiKeyRaw>>("/admin/api-keys", ct);
            return raw?.Select(r => new ApiKeyViewModel
            {
                Id        = r.Id,
                Label     = r.Label,
                CreatedAt = r.CreatedAt,
            }).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /admin/api-keys failed");
            return [];
        }
    }

    public async Task<NewApiKeyViewModel?> CreateApiKeyAsync(
        string label,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { label };
            var resp = await _http.PostAsJsonAsync("/admin/api-keys", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var raw  = await resp.Content.ReadFromJsonAsync<NewApiKeyRaw>(ct);
            return raw is null ? null : new NewApiKeyViewModel
            {
                Id        = raw.Id,
                Label     = raw.Label,
                Key       = raw.Key,
                CreatedAt = raw.CreatedAt,
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /admin/api-keys failed");
            return null;
        }
    }

    public async Task<bool> RevokeApiKeyAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/admin/api-keys/{id}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /admin/api-keys/{Id} failed", id);
            return false;
        }
    }

    // ── DELETE /admin/api-keys (batch revoke-all) ─────────────────────────────

    public async Task<int> RevokeAllApiKeysAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync("/admin/api-keys", ct);
            if (!resp.IsSuccessStatusCode) return 0;
            var raw = await resp.Content.ReadFromJsonAsync<RevokeAllRaw>(ct);
            return raw?.RevokedCount ?? 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /admin/api-keys failed");
            return 0;
        }
    }

    // ── /profiles ───────────────────────────────────────────────────────────────

    public async Task<List<ProfileViewModel>> GetProfilesAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ProfileViewModel>>("/profiles", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /profiles failed");
            return [];
        }
    }

    public async Task<ProfileViewModel?> CreateProfileAsync(
        string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { display_name = displayName, avatar_color = avatarColor, role, navigation_config = navigationConfig };
            var resp = await _http.PostAsJsonAsync("/profiles", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ProfileViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /profiles failed");
            return null;
        }
    }

    public async Task<bool> UpdateProfileAsync(
        Guid id, string displayName, string avatarColor, string role,
        string? navigationConfig = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { display_name = displayName, avatar_color = avatarColor, role, navigation_config = navigationConfig };
            var resp = await _http.PutAsJsonAsync($"/profiles/{id}", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /profiles/{Id} failed", id);
            return false;
        }
    }

    public async Task<bool> DeleteProfileAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/profiles/{id}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /profiles/{Id} failed", id);
            return false;
        }
    }

    // ── /metadata/claims + lock-claim ───────────────────────────────────────────

    public async Task<List<ClaimHistoryDto>> GetClaimHistoryAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ClaimHistoryDto>>(
                $"/metadata/claims/{entityId}", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/claims/{EntityId} failed", entityId);
            return [];
        }
    }

    public async Task<bool> LockClaimAsync(
        Guid entityId, string key, string value, CancellationToken ct = default)
    {
        try
        {
            var body = new { entity_id = entityId, claim_key = key, chosen_value = value };
            using var req = new HttpRequestMessage(new HttpMethod("PATCH"), "/metadata/lock-claim")
            {
                Content = JsonContent.Create(body),
            };
            var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PATCH /metadata/lock-claim failed");
            return false;
        }
    }

    // ── /metadata/hydrate ──────────────────────────────────────────────────────

    public async Task<HydrateResultViewModel?> TriggerHydrationAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/metadata/hydrate/{entityId}", new { }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<HydrateResultViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/hydrate/{EntityId} failed", entityId);
            return null;
        }
    }

    // ── /metadata/labels ──────────────────────────────────────────────────────

    public async Task<Dictionary<string, LabelResolveViewModel>> ResolveLabelsAsync(
        IEnumerable<string> qids, CancellationToken ct = default)
    {
        try
        {
            var request = new { qids = qids.ToList() };
            var resp = await _http.PostAsJsonAsync("/metadata/labels/resolve", request, ct);
            if (!resp.IsSuccessStatusCode)
                return new Dictionary<string, LabelResolveViewModel>();
            return await resp.Content.ReadFromJsonAsync<Dictionary<string, LabelResolveViewModel>>(ct)
                   ?? new Dictionary<string, LabelResolveViewModel>();
        }
        catch (OperationCanceledException) { return new Dictionary<string, LabelResolveViewModel>(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/labels/resolve failed");
            return new Dictionary<string, LabelResolveViewModel>();
        }
    }

    // ── /metadata/conflicts ────────────────────────────────────────────────────

    public async Task<List<ConflictViewModel>> GetConflictsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ConflictViewModel>>(
                "/metadata/conflicts", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/conflicts failed");
            return [];
        }
    }

    // ── /settings ─────────────────────────────────────────────────────────────

    public async Task<ServerGeneralSettingsDto?> GetServerGeneralAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ServerGeneralSettingsDto>("/settings/server-general", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/server-general failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> UpdateServerGeneralAsync(ServerGeneralSettingsDto settings, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("/settings/server-general", settings, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/server-general returned {Status}: {Detail}", (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/server-general failed");
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<FolderSettingsDto?> GetFolderSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<FolderSettingsDto>("/settings/folders", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/folders failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> UpdateFolderSettingsAsync(
        FolderSettingsDto settings,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { watch_directory = settings.WatchDirectory, library_root = settings.LibraryRoot };
            var resp = await _http.PutAsJsonAsync("/settings/folders", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "PUT /settings/folders returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/folders failed");
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<PathTestResultDto?> TestPathAsync(
        string            path,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { path };
            var resp = await _http.PostAsJsonAsync("/settings/test-path", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "POST /settings/test-path returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<PathTestResultDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/test-path failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<BrowseDirectoryResultDto?> BrowseDirectoryAsync(
        string?           path,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { path };
            var resp = await _http.PostAsJsonAsync("/settings/browse-directory", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "POST /settings/browse-directory returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<BrowseDirectoryResultDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/browse-directory failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(
        CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<ProviderStatusDto[]>("/settings/providers", ct);
            return raw ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/providers failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<bool> UpdateProviderAsync(
        string            name,
        bool              enabled,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var body    = new { enabled };
            var resp    = await _http.PutAsJsonAsync($"/settings/providers/{encoded}", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "PUT /settings/providers/{Name} returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/providers/{Name} failed", name);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Provider management ─────────────────────────────────────────────────

    public async Task<ProviderTestResultDto?> TestProviderAsync(
        string name, CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var resp = await _http.PostAsync($"/settings/providers/{encoded}/test", null, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /settings/providers/{Name}/test returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return new ProviderTestResultDto(false, 0, [], detail);
            }
            return await resp.Content.ReadFromJsonAsync<ProviderTestResultDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/providers/{Name}/test failed", name);
            LastError = ex.Message;
            return new ProviderTestResultDto(false, 0, [], ex.Message);
        }
    }

    public async Task<ProviderSampleResultDto?> FetchProviderSampleAsync(
        string name, string title, string? author = null,
        string? isbn = null, string? asin = null, string? mediaType = null,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var body = new { title, author, isbn, asin, media_type = mediaType };
            var resp = await _http.PostAsJsonAsync($"/settings/providers/{encoded}/sample", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /settings/providers/{Name}/sample returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<ProviderSampleResultDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/providers/{Name}/sample failed", name);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> SaveProviderConfigAsync(
        string name, ProviderConfigUpdateDto config, CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var resp = await _http.PutAsJsonAsync($"/settings/providers/{encoded}/config", config, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/providers/{Name}/config returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/providers/{Name}/config failed", name);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> DeleteProviderAsync(string name, CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(name);
            var resp = await _http.DeleteAsync($"/settings/providers/{encoded}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("DELETE /settings/providers/{Name} returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /settings/providers/{Name} failed", name);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UpdateProviderPriorityAsync(
        List<string> order, CancellationToken ct = default)
    {
        try
        {
            var body = new { order };
            var resp = await _http.PutAsJsonAsync("/settings/providers/priority", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/providers/priority returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/providers/priority failed");
            LastError = ex.Message;
            return false;
        }
    }

    // ── Activity log (/activity) ───────────────────────────────────────────

    public async Task<List<ActivityEntryViewModel>> GetRecentActivityAsync(
        int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ActivityEntryViewModel>>(
                $"/activity/recent?limit={limit}", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /activity/recent failed");
            return [];
        }
    }

    public async Task<ActivityStatsViewModel?> GetActivityStatsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ActivityStatsViewModel>("/activity/stats", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /activity/stats failed");
            return null;
        }
    }

    public async Task<PruneResultViewModel?> TriggerPruneAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/activity/prune", new { }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<PruneResultViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /activity/prune failed");
            return null;
        }
    }

    public async Task<bool> UpdateRetentionAsync(int days, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsync($"/activity/retention?days={days}", null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /activity/retention failed");
            return false;
        }
    }

    public async Task<List<ActivityEntryViewModel>> GetActivityByRunIdAsync(
        Guid runId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ActivityEntryViewModel>>(
                $"/activity/run/{runId}", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /activity/run/{RunId} failed", runId);
            return [];
        }
    }

    // ── Organization template ────────────────────────────────────────────────

    public async Task<OrganizationTemplateDto?> GetOrganizationTemplateAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<OrganizationTemplateDto>(
                "/settings/organization-template", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/organization-template failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<OrganizationTemplateDto?> UpdateOrganizationTemplateAsync(
        string template, CancellationToken ct = default)
    {
        try
        {
            var body = new { template };
            var resp = await _http.PutAsJsonAsync("/settings/organization-template", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "PUT /settings/organization-template returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<OrganizationTemplateDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/organization-template failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── Review queue (/review) ───────────────────────────────────────────

    public async Task<List<ReviewItemViewModel>> GetPendingReviewsAsync(
        int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ReviewItemViewModel>>(
                $"/review/pending?limit={limit}", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /review/pending failed");
            return [];
        }
    }

    public async Task<ReviewItemViewModel?> GetReviewItemAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ReviewItemViewModel>(
                $"/review/{id}", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /review/{Id} failed", id);
            return null;
        }
    }

    public async Task<int> GetReviewCountAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<ReviewCountDto>("/review/count", ct);
            return raw?.PendingCount ?? 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            // Debug level: this is polled for the badge count.
            _logger.LogDebug(ex, "GET /review/count failed");
            return 0;
        }
    }

    public async Task<bool> ResolveReviewItemAsync(
        Guid id, ReviewResolveRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/review/{id}/resolve", request, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /review/{Id}/resolve failed", id);
            return false;
        }
    }

    public async Task<bool> DismissReviewItemAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/review/{id}/dismiss", new { }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /review/{Id}/dismiss failed", id);
            return false;
        }
    }

    public async Task<bool> SkipUniverseAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/review/{id}/skip-universe", new { }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /review/{Id}/skip-universe failed", id);
            return false;
        }
    }

    public async Task<bool> ReclassifyMediaTypeAsync(
        Guid entityId, string mediaType, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/metadata/{entityId}/reclassify",
                new { media_type = mediaType }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/reclassify failed", entityId);
            return false;
        }
    }

    // ── Provider slots (/settings/provider-slots) ───────────────────────

    public async Task<Dictionary<string, ProviderSlotDto>?> GetProviderSlotsAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<Dictionary<string, ProviderSlotDto>>(
                "/settings/provider-slots", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/provider-slots failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> UpdateProviderSlotsAsync(
        Dictionary<string, ProviderSlotDto> slots, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("/settings/provider-slots", slots, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/provider-slots returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/provider-slots failed");
            LastError = ex.Message;
            return false;
        }
    }

    // ── Media types (/settings/media-types) ────────────────────────────────

    public async Task<MediaTypeConfigurationDto?> GetMediaTypesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MediaTypeConfigurationDto>(
                "/settings/media-types", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/media-types failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> SaveMediaTypesAsync(MediaTypeConfigurationDto config, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("/settings/media-types", config, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/media-types returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/media-types failed");
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<MediaTypeConfigurationDto?> AddMediaTypeAsync(
        MediaTypeDefinitionDto newType, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/settings/media-types/add", newType, ct);
            if (resp.IsSuccessStatusCode)
                return await resp.Content.ReadFromJsonAsync<MediaTypeConfigurationDto>(ct);

            var detail = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("POST /settings/media-types/add returned {Status}: {Detail}",
                (int)resp.StatusCode, detail);
            LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/media-types/add failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> DeleteMediaTypeAsync(string key, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/settings/media-types/{Uri.EscapeDataString(key)}", ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("DELETE /settings/media-types/{Key} returned {Status}: {Detail}",
                    key, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /settings/media-types/{Key} failed", key);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Metadata search (/metadata/search) ────────────────────────────────

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

    // ── Metadata override (/metadata/{entityId}/override) ──────────────

    public async Task<bool> OverrideMetadataAsync(
        Guid entityId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        try
        {
            var body = new { fields };
            var resp = await _http.PutAsJsonAsync($"/metadata/{entityId}/override", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /metadata/{EntityId}/override returned {Status}: {Detail}",
                    entityId, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /metadata/{EntityId}/override failed", entityId);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Hydration settings (/settings/hydration) ────────────────────────

    public async Task<HydrationSettingsDto?> GetHydrationSettingsAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HydrationSettingsDto>(
                "/settings/hydration", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/hydration failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> UpdateHydrationSettingsAsync(
        HydrationSettingsDto settings, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("/settings/hydration", settings, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /settings/hydration returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/hydration failed");
            LastError = ex.Message;
            return false;
        }
    }

    // ── Cover Art Upload ──────────────────────────────────────────────────

    public async Task<bool> UploadCoverAsync(
        Guid entityId, Stream fileStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var resp = await _http.PostAsync($"/metadata/{entityId}/cover", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /metadata/{EntityId}/cover returned {Status}: {Detail}",
                    entityId, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/cover failed", entityId);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Provider Icons ─────────────────────────────────────────────────────

    public async Task<bool> UploadProviderIconAsync(
        string name, Stream fileStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var resp = await _http.PostAsync($"/settings/providers/{name}/icon", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /settings/providers/{Name}/icon returned {Status}: {Detail}",
                    name, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/providers/{Name}/icon failed", name);
            LastError = ex.Message;
            return false;
        }
    }

    public string GetProviderIconUrl(string name) => $"/settings/providers/{name}/icon";

    // ── UI Settings (/settings/ui) ──────────────────────────────────────────

    public async Task<ResolvedUISettingsViewModel?> GetResolvedUISettingsAsync(
        string deviceClass = "web",
        string? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/settings/ui/resolved?device={WebUtility.UrlEncode(deviceClass)}";
            if (!string.IsNullOrWhiteSpace(profileId))
                url += $"&profile={WebUtility.UrlEncode(profileId)}";

            return await _http.GetFromJsonAsync<ResolvedUISettingsViewModel>(url, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/ui/resolved failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── Progress & Journey (/progress) ──────────────────────────────────

    public async Task<List<JourneyItemViewModel>> GetJourneyAsync(
        Guid? userId = null, int limit = 5, Guid? hubId = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/progress/journey?limit={limit}";
            if (userId.HasValue)
                url += $"&userId={userId.Value}";
            if (hubId.HasValue)
                url += $"&hubId={hubId.Value}";

            var raw = await _http.GetFromJsonAsync<List<JourneyItemRaw>>(url, ct);
            return raw?.Select(j => new JourneyItemViewModel
            {
                AssetId        = j.AssetId,
                WorkId         = j.WorkId,
                HubId          = j.HubId,
                Title          = j.Title ?? string.Empty,
                Author         = j.Author,
                CoverUrl       = j.CoverUrl is not null ? AbsoluteUrl(j.CoverUrl) : null,
                HeroUrl        = j.HeroUrl  is not null ? AbsoluteUrl(j.HeroUrl)  : null,
                Narrator       = j.Narrator,
                Series         = j.Series,
                SeriesPosition = j.SeriesPosition,
                Description    = j.Description,
                MediaType      = j.MediaType ?? string.Empty,
                ProgressPct    = j.ProgressPct,
                LastAccessed   = j.LastAccessed,
                HubDisplayName = j.HubDisplayName,
                ExtendedProperties = j.ExtendedProperties ?? [],
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /progress/journey failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<bool> SaveProgressAsync(
        Guid assetId, Guid? userId = null, double progressPct = 0,
        Dictionary<string, string>? extendedProperties = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                user_id = userId?.ToString(),
                progress_pct = progressPct,
                extended_properties = extendedProperties,
            };
            var resp = await _http.PutAsJsonAsync($"/progress/{assetId}", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /progress/{AssetId} failed", assetId);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Persons by Hub (/persons/by-hub) ─────────────────────────────────

    // ── POST /dev/seed-library ─────────────────────────────────────────

    public async Task<bool> SeedLibraryAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync("/dev/seed-library", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /dev/seed-library failed");
            LastError = ex.Message;
            return false;
        }
    }

    // ── GET /persons/by-hub/{hubId} ─────────────────────────────────────

    public async Task<List<PersonViewModel>> GetPersonsByRoleAsync(
        string role, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons?role={Uri.EscapeDataString(role)}&limit={limit}", ct);
            return raw?.Select(p => new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Role             = p.Role ?? string.Empty,
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = p.HeadshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = p.HasLocalHeadshot
                                   ? AbsoluteUrl($"/persons/{p.Id}/headshot")
                                   : null,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons?role={Role} failed", role);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<PersonViewModel>> GetPersonsByHubAsync(
        Guid hubId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons/by-hub/{hubId}", ct);
            return raw?.Select(p => new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Role             = p.Role ?? string.Empty,
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = p.HeadshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = p.HasLocalHeadshot
                                   ? AbsoluteUrl($"/persons/{p.Id}/headshot")
                                   : null,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/by-hub/{HubId} failed", hubId);
            LastError = ex.Message;
            return [];
        }
    }

    // -- GET /hubs/{id}/related -------------------------------------------------

    public async Task<RelatedHubsViewModel?> GetRelatedHubsAsync(
        Guid hubId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<RelatedHubsRaw>(
                $"/hubs/{hubId}/related?limit={limit}", ct);
            if (raw is null) return null;
            return new RelatedHubsViewModel
            {
                SectionTitle = raw.SectionTitle,
                Reason       = raw.Reason,
                Hubs         = raw.Hubs.Select(MapHub).ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/{HubId}/related failed", hubId);
            LastError = ex.Message;
            return null;
        }
    }

    // -- GET /persons/{id} (detail) ------------------------------------------

    public async Task<PersonDetailViewModel?> GetPersonDetailAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<PersonDetailRaw>(
                $"/persons/{personId}", ct);
            if (raw is null) return null;
            return new PersonDetailViewModel
            {
                Id               = raw.Id,
                Name             = raw.Name ?? string.Empty,
                Role             = raw.Role ?? string.Empty,
                HeadshotUrl      = raw.HeadshotUrl,
                HasLocalHeadshot = raw.HasLocalHeadshot,
                LocalHeadshotUrl = raw.HasLocalHeadshot ? AbsoluteUrl($"/persons/{raw.Id}/headshot") : null,
                Biography        = raw.Biography,
                Occupation       = raw.Occupation,
                Instagram        = raw.Instagram,
                Twitter          = raw.Twitter,
                TikTok           = raw.TikTok,
                Mastodon         = raw.Mastodon,
                Website          = raw.Website,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId} failed", personId);
            LastError = ex.Message;
            return null;
        }
    }

    // -- GET /persons/{id}/works -----------------------------------------------

    public async Task<List<HubViewModel>> GetWorksByPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<HubRaw>>(
                $"/persons/{personId}/works", ct);
            return raw?.Select(MapHub).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId}/works failed", personId);
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /hubs/parents ─────────────────────────────────────────────────────

    public async Task<List<HubViewModel>> GetParentHubsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<HubRaw>>("/hubs/parents", ct);
            return raw?.Select(MapHub).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/parents failed");
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /hubs/{id}/children ───────────────────────────────────────────────

    public async Task<List<HubViewModel>> GetChildHubsAsync(
        Guid parentHubId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<HubRaw>>(
                $"/hubs/{parentHubId}/children", ct);
            return raw?.Select(MapHub).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/{ParentHubId}/children failed", parentHubId);
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /hubs/{id}/parent ─────────────────────────────────────────────────

    public async Task<HubViewModel?> GetParentHubAsync(
        Guid hubId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"/hubs/{hubId}/parent", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadFromJsonAsync<ParentHubResponseRaw>(cancellationToken: ct);
            return raw?.ParentHub is { } hub ? MapHub(hub) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/{HubId}/parent failed", hubId);
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Most recent error message from the last failed API call.
    /// Useful for surfacing diagnostic details in the UI.
    /// Cleared on next successful call.
    /// </summary>

    // â”€â”€ Fan-out metadata search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Canonical values â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Cover from URL â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
    // ── Pass 2 (Universe Lookup) ──────────────────────────────────────────────

    public async Task<Pass2StatusDto?> GetPass2StatusAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<Pass2StatusDto>("/metadata/pass2/status", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/pass2/status failed");
            return null;
        }
    }

    public async Task<Pass2TriggerResultDto?> TriggerPass2NowAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync("/metadata/pass2/trigger", new { }, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<Pass2TriggerResultDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/pass2/trigger failed");
            return null;
        }
    }

    // ── Universe Graph (Chronicle Explorer) ───────────────────────────────────

    public async Task<UniverseGraphResponse?> GetUniverseGraphAsync(
        string qid,
        int? timelineYear = null,
        string? types = null,
        string? center = null,
        int? depth = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"universe/{Uri.EscapeDataString(qid)}/graph";
            var queryParams = new List<string>();
            if (timelineYear.HasValue) queryParams.Add($"timeline_year={timelineYear.Value}");
            if (!string.IsNullOrWhiteSpace(types)) queryParams.Add($"types={Uri.EscapeDataString(types)}");
            if (!string.IsNullOrWhiteSpace(center)) queryParams.Add($"center={Uri.EscapeDataString(center)}");
            if (depth.HasValue) queryParams.Add($"depth={depth.Value}");
            if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

            return await _http.GetFromJsonAsync<UniverseGraphResponse>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/graph failed", qid);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<IReadOnlyList<LoreDeltaResultDto>> CheckLoreDeltaAsync(
        string qid, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<LoreDeltaResultDto>>(
                $"universe/{Uri.EscapeDataString(qid)}/lore-delta", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/lore-delta failed", qid);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<IReadOnlyList<NarrativeRootDto>> GetUniversesAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<NarrativeRootDto>>("universes", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universes failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<DeepEnrichResponse?> TriggerDeepEnrichAsync(string entityQid, int depth = 2, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync(
                $"universe/entity/{Uri.EscapeDataString(entityQid)}/deep-enrich?depth={depth}",
                null, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<DeepEnrichResponse>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /universe/entity/{Qid}/deep-enrich failed", entityQid);
            LastError = ex.Message;
            return null;
        }
    }

    // ── Registry (/registry) ─────────────────────────────────────────────────

    public async Task<RegistryPageResponse?> GetRegistryItemsAsync(
        int offset = 0, int limit = 50,
        string? search = null, string? type = null, string? status = null,
        double? minConfidence = null, string? matchSource = null,
        bool duplicatesOnly = false, bool missingUniverseOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/registry/items?offset={offset}&limit={limit}";
            if (!string.IsNullOrWhiteSpace(search))
                url += $"&search={Uri.EscapeDataString(search)}";
            if (!string.IsNullOrWhiteSpace(type))
                url += $"&type={Uri.EscapeDataString(type)}";
            if (!string.IsNullOrWhiteSpace(status))
                url += $"&status={Uri.EscapeDataString(status)}";
            if (minConfidence.HasValue)
                url += $"&minConfidence={minConfidence.Value}";
            if (!string.IsNullOrWhiteSpace(matchSource))
                url += $"&matchSource={Uri.EscapeDataString(matchSource)}";
            if (duplicatesOnly)
                url += "&duplicatesOnly=true";
            if (missingUniverseOnly)
                url += "&missingUniverseOnly=true";

            return await _http.GetFromJsonAsync<RegistryPageResponse>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/items failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<RegistryItemDetailViewModel?> GetRegistryItemDetailAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<RegistryItemDetailViewModel>(
                $"/registry/items/{entityId}/detail", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/items/{EntityId}/detail failed", entityId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<RegistryStatusCountsDto?> GetRegistryStatusCountsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<RegistryStatusCountsDto>("/registry/counts", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/counts failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── Search (/search) ─────────────────────────────────────────────────────

    public async Task<SearchUniverseResponseDto?> SearchUniverseAsync(
        string query, string mediaType, int maxCandidates = 5,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new SearchUniverseRequestDto
            {
                Query         = query,
                MediaType     = mediaType,
                MaxCandidates = maxCandidates,
            };
            var resp = await _http.PostAsJsonAsync("/search/universe", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /search/universe failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<SearchUniverseResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /search/universe failed");
            return null;
        }
    }

    public async Task<SearchRetailResponseDto?> SearchRetailAsync(
        string query, string mediaType, int maxCandidates = 5,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new SearchRetailRequestDto
            {
                Query         = query,
                MediaType     = mediaType,
                MaxCandidates = maxCandidates,
            };
            var resp = await _http.PostAsJsonAsync("/search/retail", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /search/retail failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<SearchRetailResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /search/retail failed");
            return null;
        }
    }

    public async Task<ApplyMatchResponseDto?> ApplyRegistryMatchAsync(
        Guid entityId, ApplyMatchRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/registry/items/{entityId}/apply-match", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /registry/.../apply-match failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<ApplyMatchResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /registry/items/{EntityId}/apply-match failed", entityId);
            return null;
        }
    }

    public async Task<CreateManualResponseDto?> CreateManualEntryAsync(
        Guid entityId, CreateManualRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/registry/items/{entityId}/create-manual", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /registry/.../create-manual failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<CreateManualResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /registry/items/{EntityId}/create-manual failed", entityId);
            return null;
        }
    }

    public async Task<bool> DeleteRegistryItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/registry/items/{entityId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "DELETE /registry/items/{EntityId} failed", entityId);
            return false;
        }
    }

    public string? LastError { get; private set; }

    // ── Private mapping ───────────────────────────────────────────────────────

    /// <summary>
    /// Converts relative /stream/… paths stored in canonical values to absolute
    /// Engine URLs so Dashboard components can use them directly as &lt;img src&gt;.
    /// </summary>
    private string AbsoluteUrl(string value)
    {
        if (value.StartsWith('/') && _http.BaseAddress is { } baseAddr)
            return new Uri(baseAddr, value).ToString();
        return value;
    }

    private HubViewModel MapHub(HubRaw h) => HubViewModel.FromApiDto(
        h.Id,
        h.UniverseId,
        h.CreatedAt,
        h.Works.Select(w => new WorkViewModel
        {
            Id              = w.Id,
            HubId           = w.HubId,
            MediaType       = w.MediaType,
            SequenceIndex   = w.SequenceIndex,
            CanonicalValues = w.CanonicalValues.Select(cv => new CanonicalValueViewModel
            {
                Key          = cv.Key,
                Value        = AbsoluteUrl(cv.Value),
                LastScoredAt = cv.LastScoredAt,
            }).ToList(),
        }),
        displayName:   h.DisplayName,
        parentHubId:   h.ParentHubId,
        parentHubName: h.ParentHubName,
        childHubCount: h.ChildHubCount);

    // ── EPUB Reader (/read, /reader) ──────────────────────────────────

    public async Task<ProgressStateDto?> GetProgressAsync(Guid assetId, CancellationToken ct = default)
    {
        try
        {
            // Use GetAsync + manual deserialization so that 404 (no progress recorded)
            // returns null cleanly without throwing HttpRequestException.
            var resp = await _http.GetAsync($"progress/{assetId}", ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadFromJsonAsync<ProgressStateDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /progress/{AssetId} failed", assetId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<EpubBookMetadataDto?> GetBookMetadataAsync(Guid assetId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<EpubBookMetadataDto>($"read/{assetId}/metadata", ct); }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<List<EpubTocEntryDto>> GetTableOfContentsAsync(Guid assetId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<EpubTocEntryDto>>($"read/{assetId}/toc", ct) ?? []; }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<EpubChapterContentDto?> GetChapterContentAsync(Guid assetId, int chapterIndex, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<EpubChapterContentDto>($"read/{assetId}/chapter/{chapterIndex}", ct); }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<List<EpubSearchHitDto>> SearchEpubAsync(Guid assetId, string query, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(query);
            return await _http.GetFromJsonAsync<List<EpubSearchHitDto>>($"read/{assetId}/search?q={encoded}", ct) ?? [];
        }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<Guid?> ResolveWorkToAssetAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<System.Text.Json.JsonElement>($"read/resolve/{workId}", ct);
            if (result.TryGetProperty("assetId", out var prop) && Guid.TryParse(prop.GetString(), out var id))
                return id;
            return null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<List<ReaderBookmarkDto>> GetBookmarksAsync(Guid assetId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<ReaderBookmarkDto>>($"reader/{assetId}/bookmarks", ct) ?? []; }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<ReaderBookmarkDto?> CreateBookmarkAsync(Guid assetId, int chapterIndex, string? cfiPosition, string? label, CancellationToken ct = default)
    {
        try
        {
            var body = new { chapterIndex, cfiPosition, label };
            var resp = await _http.PostAsJsonAsync($"reader/{assetId}/bookmarks", body, ct);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<ReaderBookmarkDto>(cancellationToken: ct)
                : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> DeleteBookmarkAsync(Guid bookmarkId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"reader/bookmarks/{bookmarkId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public async Task<List<ReaderHighlightDto>> GetHighlightsAsync(Guid assetId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<List<ReaderHighlightDto>>($"reader/{assetId}/highlights", ct) ?? []; }
        catch (Exception ex) { LastError = ex.Message; return []; }
    }

    public async Task<ReaderHighlightDto?> CreateHighlightAsync(Guid assetId, int chapterIndex, int startOffset, int endOffset, string selectedText, string? color, string? noteText, CancellationToken ct = default)
    {
        try
        {
            var body = new { chapterIndex, startOffset, endOffset, selectedText, color, noteText };
            var resp = await _http.PostAsJsonAsync($"reader/{assetId}/highlights", body, ct);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<ReaderHighlightDto>(cancellationToken: ct)
                : null;
        }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> UpdateHighlightAsync(Guid highlightId, string? color, string? noteText, CancellationToken ct = default)
    {
        try
        {
            var body = new { color, noteText };
            var resp = await _http.PutAsJsonAsync($"reader/highlights/{highlightId}", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public async Task<bool> DeleteHighlightAsync(Guid highlightId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"reader/highlights/{highlightId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    public async Task<ReaderStatisticsDto?> GetReadingStatisticsAsync(Guid assetId, CancellationToken ct = default)
    {
        try { return await _http.GetFromJsonAsync<ReaderStatisticsDto>($"reader/{assetId}/statistics", ct); }
        catch (Exception ex) { LastError = ex.Message; return null; }
    }

    public async Task<bool> UpdateReadingStatisticsAsync(Guid assetId, ReaderStatisticsUpdateDto stats, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"reader/{assetId}/statistics", stats, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { LastError = ex.Message; return false; }
    }

    // ── Raw response shapes (mirror API Dtos.cs) ──────────────────────────────

    private sealed record StatusRaw(
        [property: JsonPropertyName("status")]  string Status,
        [property: JsonPropertyName("version")] string Version);

    private sealed record HubRaw(
        [property: JsonPropertyName("id")]              Guid           Id,
        [property: JsonPropertyName("universe_id")]     Guid?          UniverseId,
        [property: JsonPropertyName("display_name")]    string?        DisplayName,
        [property: JsonPropertyName("created_at")]      DateTimeOffset CreatedAt,
        [property: JsonPropertyName("works")]           List<WorkRaw>  Works,
        [property: JsonPropertyName("parent_hub_id")]   Guid?          ParentHubId   = null,
        [property: JsonPropertyName("parent_hub_name")] string?        ParentHubName = null,
        [property: JsonPropertyName("child_hub_count")] int            ChildHubCount = 0);

    private sealed record WorkRaw(
        [property: JsonPropertyName("id")]               Guid                      Id,
        [property: JsonPropertyName("hub_id")]           Guid?                     HubId,
        [property: JsonPropertyName("media_type")]       string                    MediaType,
        [property: JsonPropertyName("sequence_index")]   int?                      SequenceIndex,
        [property: JsonPropertyName("canonical_values")] List<CanonicalValueRaw>   CanonicalValues);

    private sealed record CanonicalValueRaw(
        [property: JsonPropertyName("key")]            string        Key,
        [property: JsonPropertyName("value")]          string        Value,
        [property: JsonPropertyName("last_scored_at")] DateTimeOffset LastScoredAt);

    private sealed record ScanRaw(
        [property: JsonPropertyName("operations")] List<OperationRaw> Operations);

    private sealed record OperationRaw(
        [property: JsonPropertyName("source_path")]      string  SourcePath,
        [property: JsonPropertyName("destination_path")] string  DestinationPath,
        [property: JsonPropertyName("operation_kind")]   string  OperationKind,
        [property: JsonPropertyName("reason")]           string? Reason);

    private sealed record SearchRawResult(
        [property: JsonPropertyName("work_id")]          Guid    WorkId,
        [property: JsonPropertyName("hub_id")]           Guid?   HubId,
        [property: JsonPropertyName("title")]            string  Title,
        [property: JsonPropertyName("author")]           string? Author,
        [property: JsonPropertyName("media_type")]       string  MediaType,
        [property: JsonPropertyName("hub_display_name")] string  HubDisplayName);

    private sealed record ApiKeyRaw(
        [property: JsonPropertyName("id")]         Guid           Id,
        [property: JsonPropertyName("label")]      string         Label,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    private sealed record NewApiKeyRaw(
        [property: JsonPropertyName("id")]         Guid           Id,
        [property: JsonPropertyName("label")]      string         Label,
        [property: JsonPropertyName("key")]        string         Key,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    private sealed record RevokeAllRaw(
        [property: JsonPropertyName("revoked_count")] int RevokedCount);

    private sealed record MetadataSearchRaw(
        [property: JsonPropertyName("provider_name")] string                    ProviderName,
        [property: JsonPropertyName("query")]         string                    Query,
        [property: JsonPropertyName("results")]       List<MetadataSearchResultRaw> Results);

    private sealed record MetadataSearchResultRaw(
        [property: JsonPropertyName("title")]            string  Title,
        [property: JsonPropertyName("author")]           string? Author,
        [property: JsonPropertyName("description")]      string? Description,
        [property: JsonPropertyName("year")]             string? Year,
        [property: JsonPropertyName("thumbnail_url")]    string? ThumbnailUrl,
        [property: JsonPropertyName("provider_item_id")] string? ProviderItemId,
        [property: JsonPropertyName("confidence")]       double  Confidence);

    private sealed record JourneyItemRaw(
        [property: JsonPropertyName("assetId")]            Guid                          AssetId,
        [property: JsonPropertyName("workId")]             Guid                          WorkId,
        [property: JsonPropertyName("hubId")]              Guid?                         HubId,
        [property: JsonPropertyName("title")]              string?                       Title,
        [property: JsonPropertyName("author")]             string?                       Author,
        [property: JsonPropertyName("coverUrl")]           string?                       CoverUrl,
        [property: JsonPropertyName("narrator")]           string?                       Narrator,
        [property: JsonPropertyName("series")]             string?                       Series,
        [property: JsonPropertyName("seriesPosition")]     string?                       SeriesPosition,
        [property: JsonPropertyName("description")]        string?                       Description,
        [property: JsonPropertyName("mediaType")]          string?                       MediaType,
        [property: JsonPropertyName("progressPct")]        double                        ProgressPct,
        [property: JsonPropertyName("lastAccessed")]       DateTimeOffset                LastAccessed,
        [property: JsonPropertyName("hubDisplayName")]     string?                       HubDisplayName,
        [property: JsonPropertyName("extendedProperties")] Dictionary<string, string>?   ExtendedProperties,
        [property: JsonPropertyName("heroUrl")]            string?                       HeroUrl);

    private sealed record PersonRaw(
        [property: JsonPropertyName("id")]                 Guid    Id,
        [property: JsonPropertyName("name")]               string? Name,
        [property: JsonPropertyName("role")]               string? Role,
        [property: JsonPropertyName("wikidata_qid")]       string? WikidataQid,
        [property: JsonPropertyName("headshot_url")]       string? HeadshotUrl,
        [property: JsonPropertyName("has_local_headshot")] bool    HasLocalHeadshot,
        [property: JsonPropertyName("biography")]          string? Biography,
        [property: JsonPropertyName("occupation")]         string? Occupation);

    private sealed record RelatedHubsRaw(
        [property: JsonPropertyName("section_title")] string       SectionTitle,
        [property: JsonPropertyName("reason")]        string       Reason,
        [property: JsonPropertyName("hubs")]          List<HubRaw> Hubs);

    private sealed record ParentHubResponseRaw(
        [property: JsonPropertyName("parentHub")] HubRaw? ParentHub);

    private sealed record PersonDetailRaw(
        [property: JsonPropertyName("id")]                 Guid            Id,
        [property: JsonPropertyName("name")]               string?         Name,
        [property: JsonPropertyName("role")]               string?         Role,
        [property: JsonPropertyName("headshot_url")]       string?         HeadshotUrl,
        [property: JsonPropertyName("has_local_headshot")] bool            HasLocalHeadshot,
        [property: JsonPropertyName("biography")]          string?         Biography,
        [property: JsonPropertyName("occupation")]         string?         Occupation,
        [property: JsonPropertyName("instagram")]          string?         Instagram,
        [property: JsonPropertyName("twitter")]            string?         Twitter,
        [property: JsonPropertyName("tiktok")]             string?         TikTok,
        [property: JsonPropertyName("mastodon")]           string?         Mastodon,
        [property: JsonPropertyName("website")]            string?         Website,
        [property: JsonPropertyName("created_at")]         DateTimeOffset  CreatedAt,
        [property: JsonPropertyName("enriched_at")]        DateTimeOffset? EnrichedAt);
}



