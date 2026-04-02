using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MediaEngine.Storage.Models;
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
                Status   = raw.Status,
                Version  = raw.Version,
                Language = raw.Language ?? "en",
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

    // ── GET /library/works ─────────────────────────────────────────────────────

    public async Task<List<WorkViewModel>> GetLibraryWorksAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("/library/works", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return [];

            var raw = await response.Content.ReadFromJsonAsync<List<LibraryWorkRaw>>(cancellationToken: ct).ConfigureAwait(false);
            if (raw is null) return [];

            return raw.Select(w => new WorkViewModel
            {
                Id              = w.Id,
                MediaType       = w.MediaType ?? "Unknown",
                SequenceIndex   = w.SequenceIndex,
                CanonicalValues = (w.CanonicalValues ?? new())
                    .Select(kv => new CanonicalValueViewModel
                    {
                        Key   = kv.Key,
                        Value = AbsoluteUrl(kv.Value),
                    })
                    .ToList(),
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/works failed");
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

    // ── Provider catalogue (/providers/catalogue) ────────────────────────────

    public async Task<IReadOnlyList<ProviderCatalogueDto>> GetProviderCatalogueAsync(
        CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<ProviderCatalogueDto[]>("/providers/catalogue", ct);
            return raw ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /providers/catalogue failed");
            LastError = ex.Message;
            return [];
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

    public async Task<List<ProviderHealthDto>> GetProviderHealthAsync(
        CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ProviderHealthDto>>(
                "/settings/providers/health", ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/providers/health failed");
            LastError = ex.Message;
            return [];
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

    public async Task<List<ActivityEntryViewModel>> GetActivityByTypesAsync(
        string[] actionTypes, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var typesParam = string.Join(",", actionTypes);
            var raw = await _http.GetFromJsonAsync<List<ActivityEntryViewModel>>(
                $"/activity/by-types?types={Uri.EscapeDataString(typesParam)}&limit={limit}", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /activity/by-types failed");
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

    // ── Pipelines (/settings/pipelines) ──────────────────────────────────

    public async Task<PipelineConfiguration?> GetPipelinesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<PipelineConfiguration>(
                "/settings/pipelines", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/pipelines failed");
            return null;
        }
    }

    public async Task<bool> SavePipelinesAsync(PipelineConfiguration pipelines, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync("/settings/pipelines", pipelines, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("PUT /settings/pipelines returned {Status}",
                    resp.StatusCode);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/pipelines failed");
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

    // ── Media File Upload ─────────────────────────────────────────────────

    public async Task<bool> UploadMediaAsync(MultipartFormDataContent content, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync("/ingestion/upload", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /ingestion/upload returned {Status}: {Detail}",
                    (int)response.StatusCode, detail);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
            }
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/upload failed");
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

    // ── GET /persons (registry list) ────────────────────────────────────

    public async Task<IReadOnlyList<PersonListItemDto>?> GetPersonsAsync(
        string? role = null, int limit = 200, CancellationToken ct = default)
    {
        try
        {
            var url = $"/persons?limit={limit}";
            if (!string.IsNullOrEmpty(role))
                url += $"&role={Uri.EscapeDataString(role)}";
            var results = await _http.GetFromJsonAsync<List<PersonListItemDto>>(url, ct);
            if (results is not null)
            {
                foreach (var p in results)
                {
                    // Build absolute headshot URL from the Engine base address
                    if (p.HasLocalHeadshot || !string.IsNullOrEmpty(p.HeadshotUrl))
                        p.HeadshotUrl = AbsoluteUrl($"/persons/{p.Id}/headshot");
                }
            }
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons failed");
            return null;
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
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = p.HeadshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = (p.HasLocalHeadshot || !string.IsNullOrEmpty(p.HeadshotUrl))
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
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = p.HeadshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = (p.HasLocalHeadshot || !string.IsNullOrEmpty(p.HeadshotUrl))
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

    public async Task<List<PersonViewModel>> GetPersonsByWorkAsync(
        Guid workId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons/by-work/{workId}", ct);
            return raw?.Select(p => new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = p.HeadshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = (p.HasLocalHeadshot || !string.IsNullOrEmpty(p.HeadshotUrl))
                                   ? AbsoluteUrl($"/persons/{p.Id}/headshot")
                                   : null,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/by-work/{WorkId} failed", workId);
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /persons/role-counts ──────────────────────────────────────────

    public async Task<Dictionary<string, int>> GetPersonRoleCountsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<Dictionary<string, int>>("/persons/role-counts", ct);
            return result ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/role-counts failed");
            return new();
        }
    }

    // ── GET /persons/presence?ids=... ─────────────────────────────────────

    public async Task<Dictionary<string, Dictionary<string, int>>> GetPersonPresenceAsync(
        IEnumerable<Guid> personIds, CancellationToken ct = default)
    {
        try
        {
            var ids = string.Join(",", personIds);
            var result = await _http.GetFromJsonAsync<Dictionary<string, Dictionary<string, int>>>(
                $"/persons/presence?ids={ids}", ct);
            return result ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/presence failed");
            return new();
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
                Roles            = raw.Roles ?? [],
                HeadshotUrl      = raw.HeadshotUrl,
                HasLocalHeadshot = raw.HasLocalHeadshot,
                LocalHeadshotUrl = (raw.HasLocalHeadshot || !string.IsNullOrEmpty(raw.HeadshotUrl)) ? AbsoluteUrl($"/persons/{raw.Id}/headshot") : null,
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

    // -- GET /persons/{id}/aliases --------------------------------------------

    /// <inheritdoc/>
    public async Task<PersonAliasesResponseDto?> GetPersonAliasesAsync(Guid personId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"persons/{personId}/aliases", ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PersonAliasesResponseDto>(cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    // ── GET /hubs/parents ─────────────────────────────────────────────────────

    public async Task<List<HubViewModel>> GetParentHubsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ParentHubRaw>>("/hubs/parents", ct);
            return raw?.Select(MapParentHub).ToList() ?? [];
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

    // ── Search results cache ────────────────────────────────────────────

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

    // ── Universe Explorer (Phase 2 modes) ────────────────────────────────────

    public async Task<UniverseCastResponse?> GetUniverseCastAsync(string qid, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<UniverseCastResponse>(
                $"universe/{Uri.EscapeDataString(qid)}/cast", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/cast failed", qid);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<UniverseAdaptationsResponse?> GetUniverseAdaptationsAsync(string qid, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<UniverseAdaptationsResponse>(
                $"universe/{Uri.EscapeDataString(qid)}/adaptations", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/adaptations failed", qid);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<UniversePathsResponse?> FindPathsAsync(
        string qid, string fromQid, string toQid, int maxHops = 4, CancellationToken ct = default)
    {
        try
        {
            var url = $"universe/{Uri.EscapeDataString(qid)}/paths" +
                      $"?from={Uri.EscapeDataString(fromQid)}" +
                      $"&to={Uri.EscapeDataString(toQid)}" +
                      $"&maxHops={maxHops}";
            return await _http.GetFromJsonAsync<UniversePathsResponse>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/paths failed", qid);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<FamilyTreeResponse?> GetFamilyTreeAsync(
        string qid, string characterQid, int generations = 3, CancellationToken ct = default)
    {
        try
        {
            var url = $"universe/{Uri.EscapeDataString(qid)}/family-tree" +
                      $"?character={Uri.EscapeDataString(characterQid)}" +
                      $"&generations={generations}";
            return await _http.GetFromJsonAsync<FamilyTreeResponse>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/family-tree failed", qid);
            LastError = ex.Message;
            return null;
        }
    }

    // ── Registry (/registry) ─────────────────────────────────────────────────

    public async Task<RegistryPageResponse?> GetRegistryItemsAsync(
        int offset = 0, int limit = 50,
        string? search = null, string? type = null, string? status = null,
        double? minConfidence = null, string? matchSource = null,
        bool? duplicatesOnly = null, bool? missingUniverseOnly = null,
        string? sort = null, int? maxDays = null,
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
            if (duplicatesOnly == true)
                url += "&duplicatesOnly=true";
            if (missingUniverseOnly == true)
                url += "&missingUniverseOnly=true";
            if (!string.IsNullOrWhiteSpace(sort))
                url += $"&sort={Uri.EscapeDataString(sort)}";
            if (maxDays.HasValue)
                url += $"&maxDays={maxDays.Value}";

            var response = await _http.GetFromJsonAsync<RegistryPageResponse>(url, ct);
            if (response?.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    if (item.CoverUrl is not null)
                        item.CoverUrl = AbsoluteUrl(item.CoverUrl);
                    if (item.HeroUrl is not null)
                        item.HeroUrl = AbsoluteUrl(item.HeroUrl);
                }
            }
            return response;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/items failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<BatchRegistryResponse?> BatchApproveRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/registry/batch/approve", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchRegistryResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch approve failed");
            return null;
        }
    }

    public async Task<BatchRegistryResponse?> BatchDeleteRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/registry/batch/delete", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchRegistryResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch delete failed");
            return null;
        }
    }

    public async Task<BatchRegistryResponse?> RejectRegistryItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/registry/items/{entityId}/reject", new { }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchRegistryResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reject registry item {EntityId} failed", entityId);
            return null;
        }
    }

    public async Task<BatchRegistryResponse?> BatchRejectRegistryItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/registry/batch/reject", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchRegistryResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch reject failed");
            return null;
        }
    }

    public async Task<RegistryItemDetailViewModel?> GetRegistryItemDetailAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var detail = await _http.GetFromJsonAsync<RegistryItemDetailViewModel>(
                $"/registry/items/{entityId}/detail", ct);
            if (detail?.CoverUrl is not null)
                detail.CoverUrl = AbsoluteUrl(detail.CoverUrl);
            if (detail?.HeroUrl is not null)
                detail.HeroUrl = AbsoluteUrl(detail.HeroUrl);
            return detail;
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

    /// <inheritdoc/>
    public async Task<RegistryFourStateCountsDto?> GetRegistryFourStateCountsAsync(
        Guid? batchId = null, CancellationToken ct = default)
    {
        try
        {
            var url = batchId.HasValue
                ? $"/registry/state-counts?batchId={batchId.Value}"
                : "/registry/state-counts";
            return await _http.GetFromJsonAsync<RegistryFourStateCountsDto>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/state-counts failed");
            LastError = ex.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetRegistryTypeCountsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("registry/type-counts", ct);
            if (!response.IsSuccessStatusCode) return new();
            return await response.Content.ReadFromJsonAsync<Dictionary<string, int>>(cancellationToken: ct) ?? new();
        }
        catch { return new(); }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IngestionBatchViewModel>> GetIngestionBatchesAsync(
        int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<IngestionBatchViewModel>>(
                $"ingestion/batches?limit={limit}", ct).ConfigureAwait(false);
            return result ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ingestion batches");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<IngestionBatchViewModel?> GetIngestionBatchByIdAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<IngestionBatchViewModel>(
                $"ingestion/batches/{id}", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch batch {Id}", id);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<int> GetBatchAttentionCountAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<AttentionCountResponse>(
                "ingestion/batches/attention-count", ct).ConfigureAwait(false);
            return result?.Count ?? 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch batch attention count");
            return 0;
        }
    }

    private sealed class AttentionCountResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public int Count { get; set; }
    }

    // ── Wikidata Aliases (/metadata/{qid}/aliases) ────────────────────────────

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

    // ── Search (/search) ─────────────────────────────────────────────────────

    public async Task<SearchUniverseResponseDto?> SearchUniverseAsync(
        string query, string mediaType, int maxCandidates = 5,
        string? localAuthor = null, CancellationToken ct = default)
    {
        try
        {
            var payload = new SearchUniverseRequestDto
            {
                Query         = query,
                MediaType     = mediaType,
                MaxCandidates = maxCandidates,
                LocalAuthor   = localAuthor,
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
        string? localTitle = null, string? localAuthor = null, string? localYear = null,
        Dictionary<string, string>? fileHints = null,
        Dictionary<string, string>? searchFields = null,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new SearchRetailRequestDto
            {
                Query         = query,
                MediaType     = mediaType,
                MaxCandidates = maxCandidates,
                LocalTitle    = localTitle,
                LocalAuthor   = localAuthor,
                LocalYear     = localYear,
                FileHints     = fileHints,
                SearchFields  = searchFields,
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

    public async Task<SearchResolveResponseDto?> SearchResolveAsync(
        string query, string mediaType, int maxCandidates,
        Dictionary<string, string>? fileHints, CancellationToken ct = default)
    {
        try
        {
            var payload = new SearchResolveRequestDto
            {
                Query         = query,
                MediaType     = mediaType,
                MaxCandidates = maxCandidates,
                FileHints     = fileHints,
            };
            var resp = await _http.PostAsJsonAsync("/search/resolve", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /search/resolve failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<SearchResolveResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /search/resolve failed");
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

    public async Task<List<RegistryItemHistoryDto>> GetItemHistoryAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<RegistryItemHistoryDto>>(
                $"/registry/items/{entityId}/history", ct);
            return result ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /registry/items/{EntityId}/history failed", entityId);
            return [];
        }
    }

    public async Task<bool> RecoverRegistryItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/registry/items/{entityId}/recover", new { }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /registry/items/{EntityId}/recover failed", entityId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MarkProvisionalAsync(Guid entityId, ProvisionalMetadataRequestDto metadata, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"registry/items/{entityId}/provisional", metadata, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MarkProvisionalAsync failed for entity {EntityId}", entityId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<BatchRegistryResponse?> AutoRegisterItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"registry/items/{entityId}/auto-register", null, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<BatchRegistryResponse>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /registry/items/{EntityId}/auto-register failed", entityId);
            return null;
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

    private HubViewModel MapParentHub(ParentHubRaw h) => HubViewModel.FromParentHub(
        h.Id,
        h.UniverseId,
        h.CreatedAt,
        displayName:   h.DisplayName,
        description:   h.Description,
        wikidataQid:   h.WikidataQid,
        childHubCount: h.ChildHubCount,
        mediaTypes:    h.MediaTypes,
        totalWorks:    h.TotalWorks);

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

    public async Task<SubmitReportResponseDto?> SubmitReportAsync(SubmitReportRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("/reports", request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<SubmitReportResponseDto>(cancellationToken: ct);
        }
        catch { return null; }
    }

    public async Task<List<ReportEntryDto>> GetReportsForEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ReportEntryDto>>($"/reports/entity/{entityId}", ct) ?? [];
        }
        catch { return []; }
    }

    public async Task<bool> ResolveReportAsync(long activityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"/reports/{activityId}/resolve", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DismissReportAsync(long activityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync($"/reports/{activityId}/dismiss", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    // ── Raw response shapes (mirror API Dtos.cs) ──────────────────────────────

    private sealed record StatusRaw(
        [property: JsonPropertyName("status")]   string  Status,
        [property: JsonPropertyName("version")]  string  Version,
        [property: JsonPropertyName("language")] string? Language);

    private sealed record HubRaw(
        [property: JsonPropertyName("id")]              Guid           Id,
        [property: JsonPropertyName("universe_id")]     Guid?          UniverseId,
        [property: JsonPropertyName("display_name")]    string?        DisplayName,
        [property: JsonPropertyName("created_at")]      DateTimeOffset CreatedAt,
        [property: JsonPropertyName("works")]           List<WorkRaw>  Works,
        [property: JsonPropertyName("parent_hub_id")]   Guid?          ParentHubId   = null,
        [property: JsonPropertyName("parent_hub_name")] string?        ParentHubName = null,
        [property: JsonPropertyName("child_hub_count")] int            ChildHubCount = 0);

    private sealed record ParentHubRaw(
        [property: JsonPropertyName("id")]               Guid           Id,
        [property: JsonPropertyName("universe_id")]      Guid?          UniverseId,
        [property: JsonPropertyName("display_name")]     string?        DisplayName,
        [property: JsonPropertyName("description")]      string?        Description,
        [property: JsonPropertyName("wikidata_qid")]     string?        WikidataQid,
        [property: JsonPropertyName("universe_status")]  string?        UniverseStatus,
        [property: JsonPropertyName("created_at")]       DateTimeOffset CreatedAt,
        [property: JsonPropertyName("child_hub_count")]  int            ChildHubCount  = 0,
        [property: JsonPropertyName("media_types")]      string?        MediaTypes     = null,
        [property: JsonPropertyName("total_works")]      int            TotalWorks     = 0);

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

    private sealed class LibraryWorkRaw
    {
        [JsonPropertyName("id")]              public Guid Id { get; set; }
        [JsonPropertyName("mediaType")]       public string? MediaType { get; set; }
        [JsonPropertyName("sequenceIndex")]   public int? SequenceIndex { get; set; }
        [JsonPropertyName("wikidataQid")]     public string? WikidataQid { get; set; }
        [JsonPropertyName("assetId")]         public Guid? AssetId { get; set; }
        [JsonPropertyName("createdAt")]       public string? CreatedAt { get; set; }
        [JsonPropertyName("canonicalValues")] public Dictionary<string, string>? CanonicalValues { get; set; }
    }

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
        [property: JsonPropertyName("id")]                 Guid          Id,
        [property: JsonPropertyName("name")]               string?       Name,
        [property: JsonPropertyName("roles")]              List<string>? Roles,
        [property: JsonPropertyName("wikidata_qid")]       string?       WikidataQid,
        [property: JsonPropertyName("headshot_url")]       string?       HeadshotUrl,
        [property: JsonPropertyName("has_local_headshot")] bool          HasLocalHeadshot,
        [property: JsonPropertyName("biography")]          string?       Biography,
        [property: JsonPropertyName("occupation")]         string?       Occupation);

    private sealed record RelatedHubsRaw(
        [property: JsonPropertyName("section_title")] string       SectionTitle,
        [property: JsonPropertyName("reason")]        string       Reason,
        [property: JsonPropertyName("hubs")]          List<HubRaw> Hubs);

    private sealed record ParentHubResponseRaw(
        [property: JsonPropertyName("parentHub")] HubRaw? ParentHub);

    private sealed record PersonDetailRaw(
        [property: JsonPropertyName("id")]                 Guid            Id,
        [property: JsonPropertyName("name")]               string?         Name,
        [property: JsonPropertyName("roles")]              List<string>?   Roles,
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

    // ── GET /ai/profile ───────────────────────────────────────────────────────

    public async Task<HardwareProfileDto?> GetAiProfileAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HardwareProfileDto>("/ai/profile", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /ai/profile failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── POST /ai/benchmark ────────────────────────────────────────────────────

    public async Task<HardwareProfileDto?> RunBenchmarkAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsync("/ai/benchmark", null, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<HardwareProfileDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ai/benchmark failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── GET /ai/enrichment/progress ───────────────────────────────────────────

    public async Task<EnrichmentProgressDto?> GetEnrichmentProgressAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<EnrichmentProgressDto>("/ai/enrichment/progress", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /ai/enrichment/progress failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── GET /ai/resources ─────────────────────────────────────────────────────

    public async Task<ResourceSnapshotDto?> GetResourceSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ResourceSnapshotDto>("/ai/resources", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /ai/resources failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── Hub Group Detail (Vault drill-down sub-pages) ─────────────────────────

    public async Task<HubGroupDetailViewModel?> GetHubGroupDetailAsync(Guid hubId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<HubGroupDetailViewModel>(
                $"/hubs/{hubId}/group-detail", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/{HubId}/group-detail failed", hubId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<HubGroupDetailViewModel?> GetArtistGroupDetailAsync(IEnumerable<Guid> hubIds, CancellationToken ct = default)
    {
        try
        {
            var idsParam = string.Join(",", hubIds);
            return await _http.GetFromJsonAsync<HubGroupDetailViewModel>(
                $"/hubs/artist-group-detail?hub_ids={idsParam}", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/artist-group-detail failed");
            LastError = ex.Message;
            return null;
        }
    }

    // ── Managed Hubs (Vault Hubs tab) ────────────────────────────────────────

    public async Task<List<ManagedHubViewModel>> GetManagedHubsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ManagedHubViewModel>>("/hubs/managed", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/managed failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<Dictionary<string, int>> GetManagedHubCountsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<Dictionary<string, int>>("/hubs/managed/counts", ct) ?? new();
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/managed/counts failed");
            LastError = ex.Message;
            return new();
        }
    }

    public async Task<List<ContentGroupViewModel>> GetContentGroupsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ContentGroupViewModel>>("/hubs/content-groups", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/content-groups failed");
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
            var url = "/hubs/system-views" + (queryParts.Count > 0 ? "?" + string.Join("&", queryParts) : "");
            return await _http.GetFromJsonAsync<List<ContentGroupViewModel>>(url, ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/system-views failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<HubItemViewModel>> GetHubItemsAsync(Guid hubId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<HubItemViewModel>>(
                $"/hubs/{hubId}/items?limit={limit}", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/{HubId}/items failed", hubId);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<bool> UpdateHubEnabledAsync(Guid hubId, bool enabled, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"/hubs/{hubId}/enabled", new { enabled }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /hubs/{HubId}/enabled failed", hubId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UpdateHubFeaturedAsync(Guid hubId, bool featured, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PutAsJsonAsync($"/hubs/{hubId}/featured", new { featured }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /hubs/{HubId}/featured failed", hubId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<HubPreviewResult?> PreviewHubRulesAsync(
        List<HubRulePredicateViewModel> rules, string matchMode, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var body = new { rules = rules.Select(r => new { field = r.Field, op = r.Op, value = r.Value, values = r.Values }).ToList(), match_mode = matchMode, limit };
            var response = await _http.PostAsJsonAsync("/hubs/preview", body, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<HubPreviewResult>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /hubs/preview failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<bool> CreateHubAsync(
        string name, List<HubRulePredicateViewModel> rules, string matchMode,
        string? sortField, string sortDirection, bool liveUpdating, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                name,
                hub_type = "Custom",
                rules = rules.Select(r => new { field = r.Field, op = r.Op, value = r.Value, values = r.Values }).ToList(),
                match_mode = matchMode,
                sort_field = sortField,
                sort_direction = sortDirection,
                live_updating = liveUpdating,
            };
            var response = await _http.PostAsJsonAsync("/hubs", body, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /hubs failed");
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UpdateHubAsync(
        Guid hubId, string? name, List<HubRulePredicateViewModel>? rules,
        string? matchMode, bool? isEnabled, bool? isFeatured, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                name,
                rules = rules?.Select(r => new { field = r.Field, op = r.Op, value = r.Value }).ToList(),
                match_mode = matchMode,
                is_enabled = isEnabled,
                is_featured = isFeatured,
            };
            var response = await _http.PutAsJsonAsync($"/hubs/{hubId}", body, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /hubs/{HubId} failed", hubId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> DeleteHubAsync(Guid hubId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"/hubs/{hubId}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /hubs/{HubId} failed", hubId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<List<HubResolvedItemViewModel>> ResolveHubAsync(Guid hubId, int? limit = null, CancellationToken ct = default)
    {
        try
        {
            var url = limit.HasValue ? $"/hubs/resolve/{hubId}?limit={limit}" : $"/hubs/resolve/{hubId}";
            return await _http.GetFromJsonAsync<List<HubResolvedItemViewModel>>(url, ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /hubs/resolve/{HubId} failed", hubId);
            LastError = ex.Message;
            return [];
        }
    }

    // ── Universe health + character data ─────────────────────────────────────

    public async Task<UniverseHealthDto?> GetUniverseHealthAsync(string qid, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<UniverseHealthRaw>($"/universe/{Uri.EscapeDataString(qid)}/health", ct);
            if (raw is null) return null;
            return new UniverseHealthDto
            {
                Qid                = raw.Qid ?? qid,
                Label              = raw.Label ?? string.Empty,
                EntitiesTotal      = raw.EntitiesTotal,
                EntitiesEnriched   = raw.EntitiesEnriched,
                EntitiesWithImages = raw.EntitiesWithImages,
                RelationshipsTotal = raw.RelationshipsTotal,
                HealthPercent      = raw.HealthPercent,
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /universe/{Qid}/health failed", qid);
            return null;
        }
    }

    public async Task<IReadOnlyList<UniverseCharacterDto>> GetUniverseCharactersAsync(string universeQid, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<UniverseCharacterRaw>>(
                $"/vault/universes/{Uri.EscapeDataString(universeQid)}/characters", ct);
            if (raw is null) return [];
            return raw.Select(r => new UniverseCharacterDto
            {
                FictionalEntityId = r.FictionalEntityId,
                CharacterName     = r.CharacterName ?? string.Empty,
                DefaultActorName  = r.DefaultActorName,
                DefaultActorId    = r.DefaultActorId,
                PortraitUrl       = r.PortraitUrl,
                ActorCount        = r.ActorCount,
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /vault/universes/{Qid}/characters failed", universeQid);
            return [];
        }
    }

    public async Task<IReadOnlyList<CharacterRoleDto>> GetPersonCharacterRolesAsync(Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CharacterRoleRaw>>(
                $"/vault/persons/{personId}/character-roles", ct);
            if (raw is null) return [];
            return raw.Select(r => new CharacterRoleDto
            {
                FictionalEntityId = r.FictionalEntityId,
                CharacterName     = r.CharacterName,
                PortraitUrl       = r.PortraitUrl,
                WorkTitle         = r.WorkTitle,
                IsDefault         = r.IsDefault,
                UniverseQid       = r.UniverseQid,
                UniverseLabel     = r.UniverseLabel,
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /vault/persons/{PersonId}/character-roles failed", personId);
            return [];
        }
    }

    public async Task SetDefaultPortraitAsync(Guid fictionalEntityId, Guid portraitId, CancellationToken ct = default)
    {
        try
        {
            await _http.PutAsJsonAsync(
                $"/vault/characters/{fictionalEntityId}/portraits/{portraitId}/default",
                new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /vault/characters/{EntityId}/portraits/{PortraitId}/default failed",
                fictionalEntityId, portraitId);
        }
    }

    public async Task<IReadOnlyList<EntityAssetDto>> GetEntityAssetsAsync(string entityId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<EntityAssetRaw>>(
                $"/vault/assets/{Uri.EscapeDataString(entityId)}", ct);
            if (raw is null) return [];
            return raw.Select(r => new EntityAssetDto
            {
                Id             = r.Id,
                EntityId       = r.EntityId ?? entityId,
                AssetType      = r.AssetType ?? string.Empty,
                ImageUrl       = r.ImageUrl,
                IsPreferred    = r.IsPreferred,
                SourceProvider = r.SourceProvider,
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /vault/assets/{EntityId} failed", entityId);
            return [];
        }
    }

    public async Task TriggerUniverseEnrichmentAsync(CancellationToken ct = default)
    {
        try
        {
            await _http.PostAsJsonAsync("/vault/enrichment/universe/trigger", new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /vault/enrichment/universe/trigger failed");
        }
    }

    // ── Timeline (/timeline) ─────────────────────────────────────────────────

    public async Task<List<EntityTimelineEventDto>?> GetEntityTimelineAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<EntityTimelineEventDto>>(
                $"/timeline/{entityId}", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /timeline/{EntityId} failed", entityId);
            return null;
        }
    }

    public async Task<List<EntityTimelineEventDto>?> GetPipelineStateAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<EntityTimelineEventDto>>(
                $"/timeline/{entityId}/pipeline", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /timeline/{EntityId}/pipeline failed", entityId);
            return null;
        }
    }

    public async Task<List<EntityFieldChangeDto>?> GetEventFieldChangesAsync(Guid entityId, Guid eventId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<EntityFieldChangeDto>>(
                $"/timeline/{entityId}/event/{eventId}/changes", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /timeline/{EntityId}/event/{EventId}/changes failed", entityId, eventId);
            return null;
        }
    }

    public async Task<bool> RevertSyncWritebackAsync(Guid entityId, Guid eventId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"/timeline/{entityId}/revert/{eventId}", new { }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /timeline/{EntityId}/revert/{EventId} failed", entityId, eventId);
            return false;
        }
    }

    public async Task<bool> RematchEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"/timeline/{entityId}/rematch", null, ct).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /timeline/{EntityId}/rematch failed", entityId);
            LastError = ex.Message;
            return false;
        }
    }

    // ── Raw deserialization models (character/universe health) ────────────────

    private sealed class UniverseHealthRaw
    {
        [JsonPropertyName("qid")]                  public string?  Qid                { get; set; }
        [JsonPropertyName("label")]                public string?  Label              { get; set; }
        [JsonPropertyName("entities_total")]       public int      EntitiesTotal      { get; set; }
        [JsonPropertyName("entities_enriched")]    public int      EntitiesEnriched   { get; set; }
        [JsonPropertyName("entities_with_images")] public int      EntitiesWithImages { get; set; }
        [JsonPropertyName("relationships_total")]  public int      RelationshipsTotal { get; set; }
        [JsonPropertyName("health_percent")]       public double   HealthPercent      { get; set; }
    }

    private sealed class UniverseCharacterRaw
    {
        [JsonPropertyName("fictional_entity_id")] public Guid    FictionalEntityId { get; set; }
        [JsonPropertyName("character_name")]      public string? CharacterName     { get; set; }
        [JsonPropertyName("default_actor_name")]  public string? DefaultActorName  { get; set; }
        [JsonPropertyName("default_actor_id")]    public Guid?   DefaultActorId    { get; set; }
        [JsonPropertyName("portrait_url")]        public string? PortraitUrl       { get; set; }
        [JsonPropertyName("actor_count")]         public int     ActorCount        { get; set; }
    }

    private sealed class CharacterRoleRaw
    {
        [JsonPropertyName("fictional_entity_id")] public Guid    FictionalEntityId { get; set; }
        [JsonPropertyName("character_name")]      public string? CharacterName     { get; set; }
        [JsonPropertyName("portrait_url")]        public string? PortraitUrl       { get; set; }
        [JsonPropertyName("work_title")]          public string? WorkTitle         { get; set; }
        [JsonPropertyName("is_default")]          public bool    IsDefault         { get; set; }
        [JsonPropertyName("universe_qid")]        public string? UniverseQid       { get; set; }
        [JsonPropertyName("universe_label")]      public string? UniverseLabel     { get; set; }
    }

    private sealed class EntityAssetRaw
    {
        [JsonPropertyName("id")]              public Guid    Id             { get; set; }
        [JsonPropertyName("entity_id")]       public string? EntityId       { get; set; }
        [JsonPropertyName("asset_type")]      public string? AssetType      { get; set; }
        [JsonPropertyName("image_url")]       public string? ImageUrl       { get; set; }
        [JsonPropertyName("is_preferred")]    public bool    IsPreferred    { get; set; }
        [JsonPropertyName("source_provider")] public string? SourceProvider { get; set; }
    }

    // ── Vault Preferences ─────────────────────────────────────────────────────

    public async Task<VaultPreferencesSettings?> GetVaultPreferencesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<VaultPreferencesSettings>("settings/ui/vault-preferences");
        }
        catch { return null; }
    }

    public async Task SaveVaultPreferencesAsync(VaultPreferencesSettings settings)
    {
        try
        {
            await _http.PutAsJsonAsync("settings/ui/vault-preferences", settings);
        }
        catch { /* swallow — preferences are non-critical */ }
    }
}



