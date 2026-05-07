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

/// <summary>
/// Strongly-typed HTTP client for the Engine API.
/// Registered via <c>AddHttpClient&lt;EngineApiClient&gt;</c> in Program.cs so the
/// base address and X-Api-Key header are injected once at startup.
/// </summary>
public sealed class EngineApiClient : IEngineApiClient
{
    private readonly HttpClient                      _http;
    private readonly ILogger<EngineApiClient>        _logger;
    private readonly StreamingServiceLogoResolver    _streamingServiceLogos;
    private readonly EngineApiFailureState           _failureState;
    private readonly SystemClient                    _systemClient;
    private readonly ProviderClient                  _providerClient;

    public EngineApiClient(
        HttpClient http,
        ILogger<EngineApiClient> logger,
        StreamingServiceLogoResolver? streamingServiceLogos = null,
        ILoggerFactory? loggerFactory = null,
        EngineApiFailureState? failureState = null)
    {
        _http                  = http;
        _logger                = logger;
        _streamingServiceLogos = streamingServiceLogos ?? new StreamingServiceLogoResolver();
        _failureState          = failureState ?? new EngineApiFailureState();
        var factory            = loggerFactory ?? NullLoggerFactory.Instance;
        _systemClient          = new SystemClient(_http, factory.CreateLogger<SystemClient>(), _failureState);
        _providerClient        = new ProviderClient(_http, factory.CreateLogger<ProviderClient>(), _failureState);
    }

    public string ToAbsoluteEngineUrl(string value) => AbsoluteUrl(value);

    public async Task<PlaybackManifestDto?> GetPlaybackManifestAsync(Guid assetId, string client = "web", CancellationToken ct = default)
    {
        var endpoint = $"GET /playback/{assetId}/manifest";
        try
        {
            var encodedClient = Uri.EscapeDataString(string.IsNullOrWhiteSpace(client) ? "web" : client);
            var response = await _http.GetAsync($"/playback/{assetId}/manifest?client={encodedClient}", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var manifest = await response.Content.ReadFromJsonAsync<PlaybackManifestDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return manifest;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /playback/{AssetId}/manifest failed", assetId);
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<TextTrackViewModel>> GetTextTracksAsync(Guid assetId, CancellationToken ct = default)
    {
        try
        {
            var tracks = await _http.GetFromJsonAsync<List<TextTrackViewModel>>($"/stream/{assetId}/text-tracks", ct);
            if (tracks is null)
                return [];

            foreach (var track in tracks)
                track.Url = AbsoluteUrl(track.Url);
            return tracks;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GET /stream/{AssetId}/text-tracks failed", assetId);
            return [];
        }
    }

    public async Task RefreshTextTracksAsync(Guid assetId, string kind, CancellationToken ct = default)
    {
        try
        {
            var encodedKind = Uri.EscapeDataString(string.IsNullOrWhiteSpace(kind) ? "lyrics" : kind);
            using var response = await _http.PostAsync($"/stream/{assetId}/text-tracks/refresh?kind={encodedKind}", null, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /stream/{AssetId}/text-tracks/refresh failed", assetId);
        }
    }

    public async Task<string?> GetLyricsAsync(Guid assetId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/stream/{assetId}/lyrics", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GET /stream/{AssetId}/lyrics failed", assetId);
            return null;
        }
    }

    public async Task<List<EncodeJobDto>> GetEncodeJobsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<EncodeJobDto>>("/playback/encode/jobs", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /playback/encode/jobs failed");
            return [];
        }
    }

    public async Task<EncodeJobDto?> QueueEncodeAsync(Guid assetId, QueueEncodeRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/playback/{assetId}/encode", request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<EncodeJobDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /playback/{AssetId}/encode failed", assetId);
            return null;
        }
    }

    public async Task<bool> CancelEncodeJobAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/playback/encode/jobs/{jobId}/cancel", new { }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /playback/encode/jobs/{JobId}/cancel failed", jobId);
            return false;
        }
    }

    public async Task<PlaybackDiagnosticsDto?> GetPlaybackDiagnosticsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<PlaybackDiagnosticsDto>("/playback/diagnostics", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /playback/diagnostics failed");
            return null;
        }
    }

    // ── GET /system/status ────────────────────────────────────────────────────

    public async Task<TranscodingSettings?> GetTranscodingSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<TranscodingSettings>("/settings/transcoding", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/transcoding failed");
            return null;
        }
    }

    public async Task<TranscodingSettings?> SaveTranscodingSettingsAsync(TranscodingSettings settings, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync("/settings/transcoding", settings, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<TranscodingSettings>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/transcoding failed");
            return null;
        }
    }

    public async Task<UserPlaybackSettingsDto?> GetPlaybackSettingsAsync(Guid profileId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<UserPlaybackSettingsDto>(
                $"/profiles/{profileId:D}/settings/playback", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "GET /profiles/{ProfileId}/settings/playback failed", profileId);
            return null;
        }
    }

    public async Task<UserPlaybackSettingsDto?> UpdatePlaybackSettingsAsync(
        Guid profileId,
        UserPlaybackSettingsDto settings,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/profiles/{profileId:D}/settings/playback", settings, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
                return null;
            }

            return await response.Content.ReadFromJsonAsync<UserPlaybackSettingsDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "PUT /profiles/{ProfileId}/settings/playback failed", profileId);
            return null;
        }
    }

    public async Task<IReadOnlyList<PluginViewModel>> GetPluginsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<PluginViewModel>>("/plugins", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /plugins failed");
            return [];
        }
    }

    public async Task<bool> SetPluginEnabledAsync(string pluginId, bool enabled, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(pluginId);
            using var response = await _http.PostAsJsonAsync($"/plugins/{encoded}/{(enabled ? "enable" : "disable")}", new { }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /plugins/{PluginId}/enable|disable failed", pluginId);
            return false;
        }
    }

    public async Task<bool> SavePluginSettingsAsync(string pluginId, Dictionary<string, JsonElement> settings, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(pluginId);
            using var response = await _http.PutAsJsonAsync($"/plugins/{encoded}/settings", settings, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /plugins/{PluginId}/settings failed", pluginId);
            return false;
        }
    }

    public async Task<PluginHealthViewModel?> CheckPluginHealthAsync(string pluginId, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(pluginId);
            using var response = await _http.PostAsJsonAsync($"/plugins/{encoded}/health", new { }, ct);
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<PluginHealthViewModel>(cancellationToken: ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /plugins/{PluginId}/health failed", pluginId);
            return null;
        }
    }

    public async Task<IReadOnlyList<PluginJobViewModel>> GetPluginJobsAsync(string pluginId, CancellationToken ct = default)
    {
        try
        {
            var encoded = Uri.EscapeDataString(pluginId);
            return await _http.GetFromJsonAsync<List<PluginJobViewModel>>($"/plugins/{encoded}/jobs", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /plugins/{PluginId}/jobs failed", pluginId);
            return [];
        }
    }

    public async Task<IReadOnlyList<PluginJobViewModel>> RunPluginSegmentDetectionJobsAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync("/plugins/jobs/segment-detection/run", new { }, ct);
            if (!response.IsSuccessStatusCode) return [];
            return await response.Content.ReadFromJsonAsync<List<PluginJobViewModel>>(cancellationToken: ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /plugins/jobs/segment-detection/run failed");
            return [];
        }
    }

    public async Task<SystemStatusViewModel?> GetSystemStatusAsync(CancellationToken ct = default)
        => await _systemClient.GetSystemStatusAsync(ct);

    // ── GET /collections ─────────────────────────────────────────────────────────────

    public async Task<AuthSettingsViewModel?> GetAuthSettingsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<AuthSettingsViewModel>("/settings/security/auth", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/security/auth failed");
            return null;
        }
    }

    public async Task<List<CollectionViewModel>> GetCollectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CollectionRaw>>("/collections", ct);
            return raw?.Select(MapCollection).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections failed");
            return [];
        }
    }

    // ── GET /library/works ─────────────────────────────────────────────────────

    public async Task<List<WorkViewModel>> GetLibraryWorksAsync(int offset = 0, int limit = 500, CancellationToken ct = default)
    {
        const string endpoint = "GET /library/works";
        try
        {
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit <= 0 ? 500 : limit, 1, 500);
            var response = await _http.GetAsync($"/library/works?offset={safeOffset}&limit={safeLimit}", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return [];
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct).ConfigureAwait(false);
            ClearFailure(endpoint);
            List<LibraryWorkRaw>? raw;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                raw = payload.Deserialize<List<LibraryWorkRaw>>();
            }
            else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var items))
            {
                raw = items.Deserialize<List<LibraryWorkRaw>>();
            }
            else
            {
                raw = [];
            }

            return raw?.Select(MapLibraryWork).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/works failed");
            RecordExceptionFailure(endpoint, ex);
            return [];
        }
    }

    // ── POST /ingestion/scan ──────────────────────────────────────────────────

    public async Task<WorkDetailViewModel?> GetWorkDetailAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<WorkDetailViewModel>($"/works/{workId:D}", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /works/{WorkId} failed", workId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<List<EditionViewModel>> GetWorkEditionsAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<EditionViewModel>>($"/works/{workId:D}/editions", ct)
                   ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /works/{WorkId}/editions failed", workId);
            LastError = ex.Message;
            return [];
        }
    }
    public async Task<ScanResultViewModel?> TriggerScanAsync(
        string? rootPath = null,
        CancellationToken ct = default)
    {
        const string endpoint = "POST /ingestion/scan";
        try
        {
            var body    = new { root_path = rootPath };
            var resp    = await _http.PostAsJsonAsync("/ingestion/scan", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, resp, ct);
                return null;
            }

            var raw     = await resp.Content.ReadFromJsonAsync<ScanRaw>(ct);
            ClearFailure(endpoint);
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
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }

    // ── POST /ingestion/library-scan ─────────────────────────────────────────

    public async Task<LibraryScanResultViewModel?> TriggerLibraryScanAsync(
        CancellationToken ct = default)
    {
        const string endpoint = "POST /ingestion/library-scan";
        try
        {
            var resp = await _http.PostAsJsonAsync("/ingestion/library-scan", new { }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, resp, ct);
                return null;
            }

            ClearFailure(endpoint);
            return await resp.Content.ReadFromJsonAsync<LibraryScanResultViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/library-scan failed");
            RecordExceptionFailure(endpoint, ex);
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
        const string endpoint = "POST /ingestion/rescan";
        try
        {
            var resp = await _http.PostAsJsonAsync("/ingestion/rescan", new { }, ct);
            if (!resp.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, resp, ct);
                return false;
            }

            ClearFailure(endpoint);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/rescan failed");
            RecordExceptionFailure(endpoint, ex);
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

    // ── GET /collections/search ─────────────────────────────────────────────────────

    public async Task<List<SearchResultViewModel>> SearchWorksAsync(
        string query,
        CancellationToken ct = default)
    {
        try
        {
            var encoded = WebUtility.UrlEncode(query);
            var raw = await _http.GetFromJsonAsync<List<SearchRawResult>>(
                $"/collections/search?q={encoded}", ct);
            return raw?.Select(r => new SearchResultViewModel
            {
                WorkId         = r.WorkId,
                CollectionId          = r.CollectionId,
                Title          = r.Title,
                Author         = r.Author,
                MediaType      = r.MediaType,
                CollectionDisplayName = r.CollectionDisplayName,
            }).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/search failed");
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
        const string endpoint = "GET /profiles";
        try
        {
            var response = await _http.GetAsync("/profiles", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return [];
            }

            var raw = await response.Content.ReadFromJsonAsync<List<ProfileViewModel>>(cancellationToken: ct);
            ClearFailure(endpoint);
            return raw?.Select(NormalizeProfile).ToList() ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /profiles failed");
            RecordExceptionFailure(endpoint, ex);
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
            var profile = await resp.Content.ReadFromJsonAsync<ProfileViewModel>(ct);
            return profile is null ? null : NormalizeProfile(profile);
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

    public async Task<ProfileViewModel?> UploadProfileAvatarAsync(
        Guid id,
        Stream fileStream,
        string fileName,
        double zoom = 1,
        CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetImageContentType(fileName));
            content.Add(fileContent, "file", fileName);
            content.Add(new StringContent(Math.Clamp(zoom, 1d, 3d).ToString(System.Globalization.CultureInfo.InvariantCulture)), "zoom");

            var resp = await _http.PostAsync($"/profiles/{id}/avatar", content, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var profile = await resp.Content.ReadFromJsonAsync<ProfileViewModel>(ct);
            return profile is null ? null : NormalizeProfile(profile);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /profiles/{Id}/avatar failed", id);
            return null;
        }
    }

    public async Task<ProfileViewModel?> RemoveProfileAvatarAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/profiles/{id}/avatar", ct);
            if (!resp.IsSuccessStatusCode) return null;
            var profile = await resp.Content.ReadFromJsonAsync<ProfileViewModel>(ct);
            return profile is null ? null : NormalizeProfile(profile);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /profiles/{Id}/avatar failed", id);
            return null;
        }
    }

    public async Task<List<ProfileExternalLoginViewModel>> GetProfileExternalLoginsAsync(
        Guid profileId,
        CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ProfileExternalLoginViewModel>>(
                $"/profiles/{profileId}/external-logins", ct);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /profiles/{ProfileId}/external-logins failed", profileId);
            return [];
        }
    }

    public async Task<ProfileExternalLoginViewModel?> LinkProfileExternalLoginAsync(
        Guid profileId,
        string provider,
        string subject,
        string? email = null,
        string? displayName = null,
        CancellationToken ct = default)
    {
        try
        {
            var body = new { provider, subject, email, display_name = displayName };
            var resp = await _http.PostAsJsonAsync($"/profiles/{profileId}/external-logins", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ProfileExternalLoginViewModel>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /profiles/{ProfileId}/external-logins failed", profileId);
            return null;
        }
    }

    public async Task<bool> UnlinkProfileExternalLoginAsync(Guid loginId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/profiles/external-logins/{loginId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /profiles/external-logins/{LoginId} failed", loginId);
            return false;
        }
    }

    // ── GET /api/v1/display/home ─────────────────────────────────────────────

    public async Task<DisplayPageDto?> GetDisplayHomeAsync(CancellationToken ct = default)
    {
        const string endpoint = "GET /api/v1/display/home";
        try
        {
            var response = await _http.GetAsync("/api/v1/display/home", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<DisplayPageDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return NormalizeDisplayPage(page);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /api/v1/display/home failed");
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }

    public async Task<DisplayPageDto?> GetDisplayBrowseAsync(
        string? lane = null,
        string? mediaType = null,
        string? grouping = null,
        string? search = null,
        int? offset = null,
        int? limit = null,
        bool? includeCatalog = null,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        const string endpoint = "GET /api/v1/display/browse";
        try
        {
            var query = new List<string>();
            AddQuery(query, "lane", lane);
            AddQuery(query, "mediaType", mediaType);
            AddQuery(query, "grouping", grouping);
            AddQuery(query, "search", search);
            AddQuery(query, "offset", offset?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddQuery(query, "limit", limit?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddQuery(query, "includeCatalog", includeCatalog?.ToString().ToLowerInvariant());
            AddQuery(query, "profileId", profileId?.ToString("D"));
            var url = "/api/v1/display/browse" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var page = await response.Content.ReadFromJsonAsync<DisplayPageDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return NormalizeDisplayPage(page);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /api/v1/display/browse failed");
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }

    public async Task<DisplayShelfPageDto?> GetDisplayShelfAsync(
        string shelfKey,
        string? lane = null,
        string? mediaType = null,
        string? grouping = null,
        string? search = null,
        string? cursor = null,
        int? offset = null,
        int? limit = null,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var query = new List<string>();
            AddQuery(query, "lane", lane);
            AddQuery(query, "mediaType", mediaType);
            AddQuery(query, "grouping", grouping);
            AddQuery(query, "search", search);
            AddQuery(query, "cursor", cursor);
            AddQuery(query, "offset", offset?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddQuery(query, "limit", limit?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            AddQuery(query, "profileId", profileId?.ToString("D"));
            var url = $"/api/v1/display/shelves/{Uri.EscapeDataString(shelfKey)}" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));
            var page = await _http.GetFromJsonAsync<DisplayShelfPageDto>(url, ct);
            return page is null ? null : page with { Shelf = NormalizeDisplayShelf(page.Shelf) };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /api/v1/display/shelves/{ShelfKey} failed", shelfKey);
            return null;
        }
    }

    public async Task<DetailPageViewModel?> GetDetailPageAsync(
        DetailEntityType entityType,
        Guid id,
        DetailPresentationContext context = DetailPresentationContext.Default,
        string? seriesId = null,
        CancellationToken ct = default)
    {
        try
        {
            var entity = Uri.EscapeDataString(entityType.ToString().ToLowerInvariant());
            var ctx = Uri.EscapeDataString(context.ToString().ToLowerInvariant());
            var seriesQuery = string.IsNullOrWhiteSpace(seriesId) ? string.Empty : $"&seriesId={Uri.EscapeDataString(seriesId)}";
            var detail = await _http.GetFromJsonAsync<DetailPageViewModel>($"/api/details/{entity}/{id:D}?context={ctx}{seriesQuery}", ct);
            return detail is null ? null : NormalizeDetailArtwork(detail);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /api/details/{EntityType}/{Id} failed", entityType, id);
            return null;
        }
    }

    public async Task<bool> SetDefaultSeriesAsync(
        DetailEntityType entityType,
        Guid id,
        string seriesId,
        string? seriesTitle = null,
        CancellationToken ct = default)
    {
        try
        {
            var entity = Uri.EscapeDataString(entityType.ToString().ToLowerInvariant());
            var response = await _http.PutAsJsonAsync(
                $"/api/details/{entity}/{id:D}/series-default",
                new SetDefaultSeriesRequest { SeriesId = seriesId, SeriesTitle = seriesTitle },
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /api/details/{EntityType}/{Id}/series-default failed", entityType, id);
            return false;
        }
    }

    public async Task<TasteProfile?> GetTasteProfileAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<TasteProfile>($"/profiles/{id}/taste", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /profiles/{Id}/taste failed", id);
            return null;
        }
    }

    // ── /metadata/claims + lock-claim ───────────────────────────────────────────

    public async Task<ProfileOverviewViewModel?> GetProfileOverviewAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var overview = await _http.GetFromJsonAsync<ProfileOverviewViewModel>($"/profiles/{id}/overview", ct);
            if (overview is not null)
                overview.Profile = NormalizeProfile(overview.Profile);
            return overview;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /profiles/{Id}/overview failed", id);
            return null;
        }
    }

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

    public async Task<List<LibraryFolderDto>?> GetLibrariesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<LibraryFolderDto>>("/settings/libraries", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/libraries failed");
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
        => await _providerClient.GetProviderCatalogueAsync(ct);

    public async Task<IReadOnlyList<ProviderStatusDto>> GetProviderStatusAsync(
        CancellationToken ct = default)
        => await _providerClient.GetProviderStatusAsync(ct);

    public async Task<bool> UpdateProviderAsync(
        string            name,
        bool              enabled,
        CancellationToken ct = default)
        => await _providerClient.UpdateProviderAsync(name, enabled, ct);

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

    public async Task<OrganizationTemplateDto?> PreviewOrganizationTemplateAsync(
        string template, CancellationToken ct = default)
    {
        try
        {
            var body = new { template };
            var resp = await _http.PostAsJsonAsync("/settings/organization-template/preview", body, ct);

            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "POST /settings/organization-template/preview returned {Status}: {Detail}",
                    (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<OrganizationTemplateDto>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /settings/organization-template/preview failed");
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
        const string endpoint = "GET /review/pending";
        try
        {
            var response = await _http.GetAsync($"/review/pending?limit={limit}", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return [];
            }

            var raw = await response.Content.ReadFromJsonAsync<List<ReviewItemViewModel>>(cancellationToken: ct);
            ClearFailure(endpoint);
            return raw ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /review/pending failed");
            RecordExceptionFailure(endpoint, ex);
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
        const string endpoint = "GET /review/count";
        try
        {
            var response = await _http.GetAsync("/review/count", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct, logAsWarning: false);
                return 0;
            }

            var raw = await response.Content.ReadFromJsonAsync<ReviewCountDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return raw?.PendingCount ?? 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            // Debug level: this is polled for the badge count.
            _logger.LogDebug(ex, "GET /review/count failed");
            RecordExceptionFailure(endpoint, ex, logAsWarning: false);
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

    // ── Vault item preferences (/vault/items/{entityId}/preferences) ────

    public async Task<bool> SaveItemPreferencesAsync(
        Guid entityId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        try
        {
            var body = new { fields };
            var resp = await _http.PutAsJsonAsync($"/library/items/{entityId}/preferences", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /library/items/{EntityId}/preferences returned {Status}: {Detail}",
                    entityId, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /library/items/{EntityId}/preferences failed", entityId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> SaveItemDisplayOverridesAsync(
        Guid entityId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        try
        {
            var body = new { fields };
            var resp = await _http.PutAsJsonAsync($"/library/items/{entityId}/display-overrides", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /library/items/{EntityId}/display-overrides returned {Status}: {Detail}",
                    entityId, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /library/items/{EntityId}/display-overrides failed", entityId);
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
        const string endpoint = "POST /ingestion/upload";
        try
        {
            var response = await _http.PostAsync("/ingestion/upload", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return false;
            }

            ClearFailure(endpoint);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /ingestion/upload failed");
            RecordExceptionFailure(endpoint, ex);
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

    public async Task<MediaEditorContextDto?> GetMediaEditorContextAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MediaEditorContextDto>($"/metadata/{entityId}/editor-context", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/{EntityId}/editor-context failed", entityId);
            return null;
        }
    }

    public async Task<MediaEditorNavigatorDto?> GetMediaEditorNavigatorAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MediaEditorNavigatorDto>($"/metadata/{entityId}/navigator", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/{EntityId}/navigator failed", entityId);
            return null;
        }
    }

    public async Task<List<MediaEditorMembershipSuggestionDto>> GetMediaEditorMembershipSuggestionsAsync(
        Guid entityId,
        string field,
        string? query = null,
        string? source = null,
        Guid? parentEntityId = null,
        string? parentValue = null,
        CancellationToken ct = default)
    {
        try
        {
            var queryParts = new List<string> { $"field={Uri.EscapeDataString(field)}" };
            if (!string.IsNullOrWhiteSpace(query))
                queryParts.Add($"query={Uri.EscapeDataString(query)}");
            if (!string.IsNullOrWhiteSpace(source))
                queryParts.Add($"source={Uri.EscapeDataString(source)}");
            if (parentEntityId.HasValue)
                queryParts.Add($"parentEntityId={Uri.EscapeDataString(parentEntityId.Value.ToString())}");
            if (!string.IsNullOrWhiteSpace(parentValue))
                queryParts.Add($"parentValue={Uri.EscapeDataString(parentValue)}");

            var url = $"/metadata/{entityId}/membership-suggestions?{string.Join("&", queryParts)}";
            return await _http.GetFromJsonAsync<List<MediaEditorMembershipSuggestionDto>>(url, ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/{EntityId}/membership-suggestions failed", entityId);
            return [];
        }
    }

    public async Task<MediaEditorMembershipPreviewDto?> PreviewMediaEditorMembershipAsync(
        Guid entityId,
        MediaEditorMembershipPreviewRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/metadata/{entityId}/membership-preview", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MediaEditorMembershipPreviewDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/membership-preview failed", entityId);
            return null;
        }
    }

    public async Task<MediaEditorMembershipPreviewDto?> ApplyMediaEditorMembershipAsync(
        Guid entityId,
        MediaEditorMembershipPreviewRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/metadata/{entityId}/membership-apply", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<MediaEditorMembershipPreviewDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/membership-apply failed", entityId);
            return null;
        }
    }

    public async Task<bool> UploadEntityArtworkAsync(
        Guid entityId, string assetType, Stream fileStream, string fileName, CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var encodedType = Uri.EscapeDataString(assetType);
            var resp = await _http.PostAsync($"/metadata/{entityId}/artwork/{encodedType}", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /metadata/{EntityId}/artwork/{AssetType} returned {Status}: {Detail}",
                    entityId, assetType, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/artwork/{AssetType} failed", entityId, assetType);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> UploadScopeArtworkVariantAsync(
        Guid entityId,
        string scopeId,
        string assetType,
        Stream fileStream,
        string fileName,
        CancellationToken ct = default)
    {
        try
        {
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            content.Add(streamContent, "file", fileName);

            var encodedScope = Uri.EscapeDataString(scopeId);
            var encodedType = Uri.EscapeDataString(assetType);
            var resp = await _http.PostAsync($"/metadata/{entityId}/artwork/{encodedScope}/{encodedType}", content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var detail = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("POST /metadata/{EntityId}/artwork/{ScopeId}/{AssetType} returned {Status}: {Detail}",
                    entityId, scopeId, assetType, (int)resp.StatusCode, detail);
                LastError = $"HTTP {(int)resp.StatusCode}: {detail}";
            }

            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /metadata/{EntityId}/artwork/{ScopeId}/{AssetType} failed", entityId, scopeId, assetType);
            LastError = ex.Message;
            return false;
        }
    }

    public Task<bool> UploadArtworkVariantAsync(
        Guid entityId, string assetType, Stream fileStream, string fileName, CancellationToken ct = default)
        => UploadEntityArtworkAsync(entityId, assetType, fileStream, fileName, ct);

    public async Task<bool> UploadScopeArtworkFromUrlAsync(
        Guid entityId,
        string scopeId,
        string assetType,
        string imageUrl,
        CancellationToken ct = default)
    {
        try
        {
            var encodedScope = Uri.EscapeDataString(scopeId);
            var encodedType = Uri.EscapeDataString(assetType);
            var response = await _http.PostAsJsonAsync(
                $"/metadata/{entityId}/artwork/{encodedScope}/{encodedType}/from-url",
                new { image_url = imageUrl },
                ct);

            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "POST /metadata/{EntityId}/artwork/{ScopeId}/{AssetType}/from-url returned {Status}: {Detail}",
                    entityId,
                    scopeId,
                    assetType,
                    (int)response.StatusCode,
                    detail);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "POST /metadata/{EntityId}/artwork/{ScopeId}/{AssetType}/from-url failed",
                entityId,
                scopeId,
                assetType);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> SetPreferredArtworkAsync(Guid variantId, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, $"/metadata/artwork/{variantId}/preferred")
            {
                Content = JsonContent.Create(new { }),
            };

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("PUT /metadata/artwork/{VariantId}/preferred returned {Status}: {Detail}",
                    variantId, (int)response.StatusCode, detail);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /metadata/artwork/{VariantId}/preferred failed", variantId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> DeleteArtworkAsync(Guid variantId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"/metadata/artwork/{variantId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("DELETE /metadata/artwork/{VariantId} returned {Status}: {Detail}",
                    variantId, (int)response.StatusCode, detail);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /metadata/artwork/{VariantId} failed", variantId);
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
        Guid? userId = null, int limit = 5, Guid? collectionId = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/progress/journey?limit={limit}";
            if (userId.HasValue)
                url += $"&userId={userId.Value}";
            if (collectionId.HasValue)
                url += $"&collectionId={collectionId.Value}";

            var raw = await _http.GetFromJsonAsync<List<JourneyItemRaw>>(url, ct);
            return raw?.Select(j => new JourneyItemViewModel
            {
                AssetId        = j.AssetId,
                WorkId         = j.WorkId,
                CollectionId          = j.CollectionId,
                Title          = j.Title ?? string.Empty,
                Author         = j.Author,
                CoverUrl       = j.CoverUrl is not null ? AbsoluteUrl(j.CoverUrl) : null,
                BackgroundUrl  = j.BackgroundUrl is not null ? AbsoluteUrl(j.BackgroundUrl) : null,
                BannerUrl      = j.BannerUrl is not null ? AbsoluteUrl(j.BannerUrl) : null,
                HeroUrl        = j.HeroUrl  is not null ? AbsoluteUrl(j.HeroUrl)  : null,
                LogoUrl        = j.LogoUrl  is not null ? AbsoluteUrl(j.LogoUrl)  : null,
                CoverWidthPx = j.CoverWidthPx,
                CoverHeightPx = j.CoverHeightPx,
                BackgroundWidthPx = j.BackgroundWidthPx,
                BackgroundHeightPx = j.BackgroundHeightPx,
                BannerWidthPx = j.BannerWidthPx,
                BannerHeightPx = j.BannerHeightPx,
                Narrator       = j.Narrator,
                Series         = j.Series,
                SeriesPosition = j.SeriesPosition,
                Description    = j.Description,
                MediaType      = j.MediaType ?? string.Empty,
                ProgressPct    = j.ProgressPct,
                LastAccessed   = j.LastAccessed,
                CollectionDisplayName = j.CollectionDisplayName,
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

    // ── Persons by Collection (/persons/by-collection) ─────────────────────────────────

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

    // ── GET /persons (libraryItem list) ────────────────────────────────────

    public async Task<IReadOnlyList<PersonListItemDto>?> GetPersonsAsync(
        string? role = null, int offset = 0, int limit = 200, CancellationToken ct = default)
    {
        try
        {
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit <= 0 ? 200 : limit, 1, 500);
            var url = $"/persons?offset={safeOffset}&limit={safeLimit}";
            if (!string.IsNullOrEmpty(role))
                url += $"&role={Uri.EscapeDataString(role)}";
            var payload = await _http.GetFromJsonAsync<JsonElement>(url, ct);
            List<PersonListItemDto>? results;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                results = payload.Deserialize<List<PersonListItemDto>>();
            }
            else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var items))
            {
                results = items.Deserialize<List<PersonListItemDto>>();
            }
            else
            {
                results = [];
            }
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

    // ── GET /persons/by-collection/{collectionId} ─────────────────────────────────────

    public async Task<List<PersonViewModel>> GetPersonsByRoleAsync(
        string role, int limit = 50, CancellationToken ct = default)
    {
        try
        {
            var safeLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 500);
            var payload = await _http.GetFromJsonAsync<JsonElement>(
                $"/persons?role={Uri.EscapeDataString(role)}&limit={safeLimit}", ct);
            List<PersonRaw>? raw;
            if (payload.ValueKind == JsonValueKind.Array)
            {
                raw = payload.Deserialize<List<PersonRaw>>();
            }
            else if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var items))
            {
                raw = items.Deserialize<List<PersonRaw>>();
            }
            else
            {
                raw = [];
            }
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons?role={Role} failed", role);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<PersonViewModel>> GetPersonsByCollectionAsync(
        Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<PersonRaw>>(
                $"/persons/by-collection/{collectionId}", ct);
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
            }).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/by-collection/{CollectionId} failed", collectionId);
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
            return raw?.Select(p =>
            {
                var headshotUrl = ResolvePersonHeadshotUrl(p);
                return new PersonViewModel
            {
                Id               = p.Id,
                Name             = p.Name ?? string.Empty,
                Roles            = p.Roles ?? [],
                WikidataQid      = p.WikidataQid,
                HeadshotUrl      = headshotUrl,
                HasLocalHeadshot = p.HasLocalHeadshot,
                LocalHeadshotUrl = headshotUrl,
                Biography        = p.Biography,
                Occupation       = p.Occupation,
            };
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

    // -- GET /collections/{id}/related -------------------------------------------------

    public async Task<RelatedCollectionsViewModel?> GetRelatedCollectionsAsync(
        Guid collectionId, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<RelatedCollectionsRaw>(
                $"/collections/{collectionId}/related?limit={limit}", ct);
            if (raw is null) return null;
            return new RelatedCollectionsViewModel
            {
                SectionTitle = raw.SectionTitle,
                Reason       = raw.Reason,
                Collections         = raw.Collections.Select(MapCollection).ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/related failed", collectionId);
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
                DateOfBirth      = raw.DateOfBirth,
                DateOfDeath      = raw.DateOfDeath,
                PlaceOfBirth     = raw.PlaceOfBirth,
                PlaceOfDeath     = raw.PlaceOfDeath,
                Nationality      = raw.Nationality,
                WikidataQid      = raw.WikidataQid,
                Instagram        = raw.Instagram,
                Twitter          = raw.Twitter,
                TikTok           = raw.TikTok,
                Mastodon         = raw.Mastodon,
                Website          = raw.Website,
                IsGroup          = raw.IsGroup,
                GroupMembers     = raw.GroupMembers?.Select(MapGroupMember).ToList() ?? [],
                MemberOfGroups   = raw.MemberOfGroups?.Select(MapGroupMember).ToList() ?? [],
                BannerUrl        = raw.BannerUrl is not null ? AbsoluteUrl(raw.BannerUrl) : null,
                BackgroundUrl    = raw.BackgroundUrl is not null ? AbsoluteUrl(raw.BackgroundUrl) : null,
                LogoUrl          = raw.LogoUrl is not null ? AbsoluteUrl(raw.LogoUrl) : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId} failed", personId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<List<PersonLibraryCreditViewModel>> GetPersonLibraryCreditsAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var credits = await _http.GetFromJsonAsync<List<PersonLibraryCreditViewModel>>(
                $"/persons/{personId}/library-credits", ct);

            NormalizePersonLibraryCredits(credits);
            return credits ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /persons/{PersonId}/library-credits failed", personId);
            LastError = ex.Message;
            return [];
        }
    }

    // -- GET /persons/{id}/works -----------------------------------------------

    public async Task<List<CollectionViewModel>> GetWorksByPersonAsync(
        Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CollectionRaw>>(
                $"/persons/{personId}/works", ct);
            return raw?.Select(MapCollection).ToList() ?? [];
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
            var result = await response.Content.ReadFromJsonAsync<PersonAliasesResponseDto>(cancellationToken: ct);
            if (result is not null)
            {
                foreach (var alias in result.Aliases)
                {
                    if (!string.IsNullOrWhiteSpace(alias.HeadshotUrl))
                        alias.HeadshotUrl = AbsoluteUrl(alias.HeadshotUrl);
                }
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    // ── GET /collections/parents ─────────────────────────────────────────────────────

    public async Task<List<CollectionViewModel>> GetParentCollectionsAsync(CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<ParentCollectionRaw>>("/collections/parents", ct);
            return raw?.Select(MapParentCollection).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/parents failed");
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /collections/{id}/children ───────────────────────────────────────────────

    public async Task<List<CollectionViewModel>> GetChildCollectionsAsync(
        Guid parentCollectionId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CollectionRaw>>(
                $"/collections/{parentCollectionId}/children", ct);
            return raw?.Select(MapCollection).ToList() ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{ParentCollectionId}/children failed", parentCollectionId);
            LastError = ex.Message;
            return [];
        }
    }

    // ── GET /collections/{id}/parent ─────────────────────────────────────────────────

    public async Task<CollectionViewModel?> GetParentCollectionAsync(
        Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"/collections/{collectionId}/parent", ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            var raw = await resp.Content.ReadFromJsonAsync<ParentCollectionResponseRaw>(cancellationToken: ct);
            return raw?.ParentCollection is { } collection ? MapCollection(collection) : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/parent failed", collectionId);
            LastError = ex.Message;
            return null;
        }
    }

    /// <summary>
    /// Most recent error message from the last failed API call.
    /// Useful for surfacing diagnostic details in the UI.
    /// Cleared on next successful call.
    /// </summary>

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

    // ── Retag Sweep (Auto re-tag) ─────────────────────────────────────────────

    public async Task<RetagSweepStateDto?> GetRetagSweepStateAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync("/maintenance/retag-sweep/state", ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc    = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var hasPending = root.TryGetProperty("has_pending_diff", out var hpd) && hpd.GetBoolean();

            var diffList = new List<RetagFieldDiffDto>();
            if (root.TryGetProperty("pending_diff", out var pd) && pd.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in pd.EnumerateArray())
                {
                    var mt = item.GetProperty("media_type").GetString() ?? string.Empty;
                    var added = item.TryGetProperty("added_fields", out var af) && af.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? af.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                        : new List<string>();
                    var removed = item.TryGetProperty("removed_fields", out var rf) && rf.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? rf.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                        : new List<string>();
                    diffList.Add(new RetagFieldDiffDto(mt, added, removed));
                }
            }

            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("current_hashes", out var ch) && ch.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (var prop in ch.EnumerateObject())
                    hashes[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            return new RetagSweepStateDto(hasPending, diffList, hashes);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /maintenance/retag-sweep/state failed");
            return null;
        }
    }

    public async Task<bool> ApplyRetagSweepPendingAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync("/maintenance/retag-sweep/apply", content: null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /maintenance/retag-sweep/apply failed");
            return false;
        }
    }

    public async Task<bool> RunRetagSweepNowAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync("/maintenance/retag-sweep/run-now", content: null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /maintenance/retag-sweep/run-now failed");
            return false;
        }
    }

    public async Task<bool> RunInitialSweepAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync("/maintenance/initial-sweep/run", content: null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /maintenance/initial-sweep/run failed");
            return false;
        }
    }

    public async Task<bool> RetryRetagForAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"/maintenance/retag-sweep/retry/{assetId}", content: null, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /maintenance/retag-sweep/retry/{Id} failed", assetId);
            return false;
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

    // ── Vault items (/vault/items) ───────────────────────────────────────────

    public async Task<LibraryCatalogPageResponse?> GetLibraryCatalogItemsAsync(
        int offset = 0, int limit = 50,
        string? search = null, string? type = null, string? status = null,
        double? minConfidence = null, string? matchSource = null,
        bool? duplicatesOnly = null, bool? missingUniverseOnly = null,
        string? sort = null, int? maxDays = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"/library/items?offset={offset}&limit={limit}";
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

            var response = await _http.GetFromJsonAsync<LibraryCatalogPageResponse>(url, ct);
            if (response?.Items is not null)
            {
                foreach (var item in response.Items)
                {
                    if (item.CoverUrl is not null)
                        item.CoverUrl = AbsoluteUrl(item.CoverUrl);
                    if (item.BackgroundUrl is not null)
                        item.BackgroundUrl = AbsoluteUrl(item.BackgroundUrl);
                    if (item.BannerUrl is not null)
                        item.BannerUrl = AbsoluteUrl(item.BannerUrl);
                    if (item.HeroUrl is not null)
                        item.HeroUrl = AbsoluteUrl(item.HeroUrl);
                }
            }
            return response;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/items failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<BatchLibraryItemResponse?> BatchApproveLibraryCatalogItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/library/items/batch/approve", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchLibraryItemResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch approve failed");
            return null;
        }
    }

    public async Task<BatchLibraryItemResponse?> BatchDeleteLibraryCatalogItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/library/items/batch/delete", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchLibraryItemResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch delete failed");
            return null;
        }
    }

    public async Task<BatchLibraryItemResponse?> RejectLibraryCatalogItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/library/items/{entityId}/reject", new { }, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchLibraryItemResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reject libraryItem item {EntityId} failed", entityId);
            return null;
        }
    }

    public async Task<BatchLibraryItemResponse?> BatchRejectLibraryCatalogItemsAsync(Guid[] entityIds, CancellationToken ct = default)
    {
        try
        {
            var request = new { entity_ids = entityIds };
            var response = await _http.PostAsJsonAsync("/library/items/batch/reject", request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<BatchLibraryItemResponse>(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch reject failed");
            return null;
        }
    }

    public async Task<LibraryItemDetailViewModel?> GetLibraryItemDetailAsync(
        Guid entityId, CancellationToken ct = default)
    {
        var endpoint = $"GET /library/items/{entityId}/detail";
        try
        {
            var response = await _http.GetAsync($"/library/items/{entityId}/detail", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var detail = await response.Content.ReadFromJsonAsync<LibraryItemDetailViewModel>(cancellationToken: ct);
            if (detail?.CoverUrl is not null)
                detail.CoverUrl = AbsoluteUrl(detail.CoverUrl);
            if (detail?.BackgroundUrl is not null)
                detail.BackgroundUrl = AbsoluteUrl(detail.BackgroundUrl);
            if (detail?.BannerUrl is not null)
                detail.BannerUrl = AbsoluteUrl(detail.BannerUrl);
            if (detail?.HeroUrl is not null)
                detail.HeroUrl = AbsoluteUrl(detail.HeroUrl);
            ClearFailure(endpoint);
            return detail;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/items/{EntityId}/detail failed", entityId);
            RecordExceptionFailure(endpoint, ex);
            return null;
        }
    }

    public async Task<LibraryItemStatusCountsDto?> GetLibraryItemStatusCountsAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<LibraryItemStatusCountsDto>("/library/items/counts", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/items/counts failed");
            LastError = ex.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<LibraryItemLifecycleCountsDto?> GetLibraryItemLifecycleCountsAsync(
        Guid? batchId = null, CancellationToken ct = default)
    {
        try
        {
            var url = batchId.HasValue
                ? $"/library/items/state-counts?batchId={batchId.Value}"
                : "/library/items/state-counts";
            return await _http.GetFromJsonAsync<LibraryItemLifecycleCountsDto>(url, ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/items/state-counts failed");
            LastError = ex.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetLibraryItemTypeCountsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync("library/items/type-counts", ct);
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

    public async Task<ApplyMatchResponseDto?> ApplyLibraryItemMatchAsync(
        Guid entityId, ApplyMatchRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync(
                $"/library/items/{entityId}/apply-match", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /library/items/.../apply-match failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<ApplyMatchResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/apply-match failed", entityId);
            return null;
        }
    }

    public async Task<ItemCanonicalSearchResponseDto?> SearchItemCanonicalAsync(
        Guid entityId, ItemCanonicalSearchRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/library/items/{entityId}/canonical-search", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /library/items/.../canonical-search failed: {resp.StatusCode}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<ItemCanonicalSearchResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/canonical-search failed", entityId);
            return null;
        }
    }

    public async Task<ItemCanonicalApplyResponseDto?> ApplyItemCanonicalAsync(
        Guid entityId, ItemCanonicalApplyRequestDto request, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/library/items/{entityId}/canonical-apply", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /library/items/.../canonical-apply failed: {resp.StatusCode}";
                return null;
            }

            return await resp.Content.ReadFromJsonAsync<ItemCanonicalApplyResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/canonical-apply failed", entityId);
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
                $"/library/items/{entityId}/create-manual", request, ct);
            if (!resp.IsSuccessStatusCode)
            {
                LastError = $"POST /library/items/.../create-manual failed: {resp.StatusCode}";
                return null;
            }
            return await resp.Content.ReadFromJsonAsync<CreateManualResponseDto>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/create-manual failed", entityId);
            return null;
        }
    }

    public async Task<bool> DeleteLibraryCatalogItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.DeleteAsync($"/library/items/{entityId}", ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            LastError = ex.Message;
            _logger.LogWarning(ex, "DELETE /library/items/{EntityId} failed", entityId);
            return false;
        }
    }

    public async Task<List<LibraryItemHistoryDto>> GetItemHistoryAsync(
        Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<LibraryItemHistoryDto>>(
                $"/library/items/{entityId}/history", ct);
            return result ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/items/{EntityId}/history failed", entityId);
            return [];
        }
    }

    public async Task<bool> RecoverLibraryCatalogItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsJsonAsync($"/library/items/{entityId}/recover", new { }, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/recover failed", entityId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> MarkProvisionalAsync(Guid entityId, ProvisionalMetadataRequestDto metadata, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"/library/items/{entityId}/provisional", metadata, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MarkProvisionalAsync failed for entity {EntityId}", entityId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<BatchLibraryItemResponse?> AutoMatchLibraryItemAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.PostAsync($"/library/items/{entityId}/auto-register", null, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<BatchLibraryItemResponse>(ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/items/{EntityId}/auto-register failed", entityId);
            return null;
        }
    }

    public string? LastError
    {
        get => _failureState.LastError;
        private set => _failureState.SetError(value);
    }

    public int? LastStatusCode => _failureState.LastStatusCode;

    public string? LastFailedEndpoint => _failureState.LastFailedEndpoint;

    public string? LastFailureKind => _failureState.LastFailureKind;

    private void ClearFailure(string endpoint)
        => _failureState.Clear(endpoint);

    private async Task RecordHttpFailureAsync(
        string endpoint,
        HttpResponseMessage response,
        CancellationToken ct,
        bool logAsWarning = true)
        => await _failureState.RecordHttpFailureAsync(endpoint, response, _logger, ct, logAsWarning);

    private void RecordExceptionFailure(string endpoint, Exception ex, bool logAsWarning = true)
        => _failureState.RecordExceptionFailure(endpoint, ex, _logger, logAsWarning);

    private static async Task<string> ReadProblemSummaryAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var raw = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return response.ReasonPhrase ?? "Request failed";
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return raw;
            }

            var title = TryGetString(doc.RootElement, "title");
            var detail = TryGetString(doc.RootElement, "detail");
            var traceId = TryGetString(doc.RootElement, "traceId") ?? TryGetString(doc.RootElement, "trace_id");
            var parts = new[] { title, detail, string.IsNullOrWhiteSpace(traceId) ? null : $"Trace: {traceId}" }
                .Where(static part => !string.IsNullOrWhiteSpace(part));
            var summary = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(summary) ? raw : summary;
        }
        catch (JsonException)
        {
            return raw;
        }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    // ── Private mapping ───────────────────────────────────────────────────────

    private void NormalizeCollectionGroupDetail(CollectionGroupDetailViewModel? detail)
    {
        if (detail is null)
            return;

        if (detail.CoverUrl is not null)
            detail.CoverUrl = AbsoluteUrl(detail.CoverUrl);
        if (detail.BackgroundUrl is not null)
            detail.BackgroundUrl = AbsoluteUrl(detail.BackgroundUrl);
        if (detail.BannerUrl is not null)
            detail.BannerUrl = AbsoluteUrl(detail.BannerUrl);
        if (detail.ArtistPhotoUrl is not null)
            detail.ArtistPhotoUrl = AbsoluteUrl(detail.ArtistPhotoUrl);

        foreach (var cast in detail.TopCast)
        {
            if (cast.HeadshotUrl is not null)
                cast.HeadshotUrl = AbsoluteUrl(cast.HeadshotUrl);
            if (cast.ActorHeadshotUrl is not null)
                cast.ActorHeadshotUrl = AbsoluteUrl(cast.ActorHeadshotUrl);
            if (cast.CharacterImageUrl is not null)
                cast.CharacterImageUrl = AbsoluteUrl(cast.CharacterImageUrl);
            foreach (var character in cast.Characters)
            {
                if (character.PortraitUrl is not null)
                    character.PortraitUrl = AbsoluteUrl(character.PortraitUrl);
            }
        }

        foreach (var season in detail.Seasons)
        {
            if (season.CoverUrl is not null)
                season.CoverUrl = AbsoluteUrl(season.CoverUrl);

            foreach (var episode in season.Episodes)
            {
                NormalizeCollectionGroupWork(episode);
            }
        }

        foreach (var work in detail.Works)
        {
            NormalizeCollectionGroupWork(work);
        }
    }

    private void NormalizeCollectionGroupWork(CollectionGroupWorkViewModel work)
    {
        if (work.CoverUrl is not null)
            work.CoverUrl = AbsoluteUrl(work.CoverUrl);
        if (work.BackgroundUrl is not null)
            work.BackgroundUrl = AbsoluteUrl(work.BackgroundUrl);
        if (work.BannerUrl is not null)
            work.BannerUrl = AbsoluteUrl(work.BannerUrl);
        if (work.HeroUrl is not null)
            work.HeroUrl = AbsoluteUrl(work.HeroUrl);
    }

    private void NormalizeCastCredits(List<CollectionGroupPersonViewModel>? castCredits)
    {
        if (castCredits is null)
            return;

        foreach (var cast in castCredits)
        {
            if (cast.HeadshotUrl is not null)
                cast.HeadshotUrl = AbsoluteUrl(cast.HeadshotUrl);
            if (cast.ActorHeadshotUrl is not null)
                cast.ActorHeadshotUrl = AbsoluteUrl(cast.ActorHeadshotUrl);
            if (cast.CharacterImageUrl is not null)
                cast.CharacterImageUrl = AbsoluteUrl(cast.CharacterImageUrl);

            foreach (var character in cast.Characters)
            {
                if (character.PortraitUrl is not null)
                    character.PortraitUrl = AbsoluteUrl(character.PortraitUrl);
            }
        }
    }

    private void NormalizePersonLibraryCredits(List<PersonLibraryCreditViewModel>? credits)
    {
        if (credits is null)
            return;

        foreach (var credit in credits)
        {
            if (credit.CoverUrl is not null)
                credit.CoverUrl = AbsoluteUrl(credit.CoverUrl);

            foreach (var character in credit.Characters)
            {
                if (character.PortraitUrl is not null)
                    character.PortraitUrl = AbsoluteUrl(character.PortraitUrl);
            }
        }
    }

    private DisplayPageDto? NormalizeDisplayPage(DisplayPageDto? page)
    {
        if (page is null)
            return null;

        return page with
        {
            Hero = page.Hero is null ? null : page.Hero with { Artwork = NormalizeDisplayArtwork(page.Hero.Artwork) },
            Shelves = page.Shelves
                .Select(shelf => shelf with { Items = shelf.Items.Select(NormalizeDisplayCard).ToList() })
                .ToList(),
            Catalog = page.Catalog.Select(NormalizeDisplayCard).ToList(),
        };
    }

    private DisplayCardDto NormalizeDisplayCard(DisplayCardDto card) =>
        card with { Artwork = NormalizeDisplayArtwork(card.Artwork) };

    private DisplayShelfDto NormalizeDisplayShelf(DisplayShelfDto shelf) =>
        shelf with { Items = shelf.Items.Select(NormalizeDisplayCard).ToList() };

    private DisplayArtworkDto NormalizeDisplayArtwork(DisplayArtworkDto artwork) =>
        artwork with
        {
            CoverUrl = artwork.CoverUrl is null ? null : AbsoluteUrl(artwork.CoverUrl),
            SquareUrl = artwork.SquareUrl is null ? null : AbsoluteUrl(artwork.SquareUrl),
            BannerUrl = artwork.BannerUrl is null ? null : AbsoluteUrl(artwork.BannerUrl),
            BackgroundUrl = artwork.BackgroundUrl is null ? null : AbsoluteUrl(artwork.BackgroundUrl),
            LogoUrl = artwork.LogoUrl is null ? null : AbsoluteUrl(artwork.LogoUrl),
        };

    private DetailPageViewModel NormalizeDetailArtwork(DetailPageViewModel detail)
    {
        var artwork = detail.Artwork;
        return new DetailPageViewModel
        {
            Id = detail.Id,
            EntityType = detail.EntityType,
            PresentationContext = detail.PresentationContext,
            Title = detail.Title,
            Subtitle = detail.Subtitle,
            Tagline = detail.Tagline,
            Description = detail.Description,
            PersonDetails = detail.PersonDetails,
            Artwork = new ArtworkSet
            {
                BackdropUrl = NormalizeOptionalUrl(artwork.BackdropUrl),
                BannerUrl = NormalizeOptionalUrl(artwork.BannerUrl),
                PosterUrl = NormalizeOptionalUrl(artwork.PosterUrl),
                CoverUrl = NormalizeOptionalUrl(artwork.CoverUrl),
                LogoUrl = NormalizeOptionalUrl(artwork.LogoUrl),
                PortraitUrl = NormalizeOptionalUrl(artwork.PortraitUrl),
                CharacterImageUrl = NormalizeOptionalUrl(artwork.CharacterImageUrl),
                RelatedArtworkUrls = artwork.RelatedArtworkUrls.Select(AbsoluteUrl).ToList(),
                DominantColors = artwork.DominantColors,
                PrimaryColor = artwork.PrimaryColor,
                SecondaryColor = artwork.SecondaryColor,
                AccentColor = artwork.AccentColor,
                HeroArtwork = NormalizeHeroArtwork(artwork.HeroArtwork),
                PresentationMode = artwork.PresentationMode,
                Source = artwork.Source,
            },
            HeroBrand = NormalizeHeroBrand(detail.HeroBrand),
            Progress = detail.Progress,
            OwnedFormats = detail.OwnedFormats.Select(format => new OwnedFormatViewModel
            {
                Id = format.Id,
                FormatType = format.FormatType,
                DisplayName = format.DisplayName,
                CoverUrl = NormalizeOptionalUrl(format.CoverUrl),
                EditionTitle = format.EditionTitle,
                Publisher = format.Publisher,
                ReleaseDate = format.ReleaseDate,
                PrimaryContributor = format.PrimaryContributor,
                FileFormat = format.FileFormat,
                Runtime = format.Runtime,
                PageCount = format.PageCount,
                ChapterCount = format.ChapterCount,
                Progress = format.Progress,
                Actions = format.Actions,
            }).ToList(),
            MultiFormatState = detail.MultiFormatState,
            ReadingListeningSync = detail.ReadingListeningSync,
            SyncCapability = detail.SyncCapability,
            SeriesPlacement = NormalizeSeriesPlacement(detail.SeriesPlacement),
            Metadata = detail.Metadata,
            PrimaryActions = detail.PrimaryActions,
            SecondaryActions = detail.SecondaryActions,
            OverflowActions = detail.OverflowActions,
            ContributorGroups = detail.ContributorGroups.Select(NormalizeCreditGroup).ToList(),
            PreviewContributors = detail.PreviewContributors.Select(NormalizeCredit).ToList(),
            CharacterGroups = detail.CharacterGroups.Select(group => new CharacterGroupViewModel
            {
                Title = group.Title,
                GroupType = group.GroupType,
                Characters = group.Characters.Select(NormalizeCredit).ToList(),
            }).ToList(),
            PreviewCharacters = detail.PreviewCharacters.Select(NormalizeCredit).ToList(),
            RelationshipStrip = detail.RelationshipStrip,
            Tabs = detail.Tabs,
            MediaGroups = detail.MediaGroups.Select(group => new MediaGroupingViewModel
            {
                Key = group.Key,
                Title = group.Title,
                Items = group.Items.Select(item => new MediaGroupingItemViewModel
                {
                    Id = item.Id,
                    EntityType = item.EntityType,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Description = item.Description,
                    ArtworkUrl = NormalizeOptionalUrl(item.ArtworkUrl),
                    TrackNumber = item.TrackNumber,
                    Duration = item.Duration,
                    Artist = item.Artist,
                    IsExplicit = item.IsExplicit,
                    Quality = item.Quality,
                    ProgressPercent = item.ProgressPercent,
                    Metadata = item.Metadata,
                    Actions = item.Actions,
                    IsOwned = item.IsOwned,
                    ProgressState = item.ProgressState,
                }).ToList(),
            }).ToList(),
            IdentityStatus = detail.IdentityStatus,
            LibraryStatus = detail.LibraryStatus,
            IsAdminView = detail.IsAdminView,
        };
    }

    private HeroArtworkViewModel NormalizeHeroArtwork(HeroArtworkViewModel? heroArtwork)
    {
        if (heroArtwork is null)
            return new HeroArtworkViewModel();

        return new HeroArtworkViewModel
        {
            Url = NormalizeOptionalUrl(heroArtwork.Url),
            Mode = heroArtwork.Mode,
            HasImage = heroArtwork.HasImage && !string.IsNullOrWhiteSpace(heroArtwork.Url),
            AspectRatio = heroArtwork.AspectRatio,
            BackgroundPosition = heroArtwork.BackgroundPosition,
            MobilePosition = heroArtwork.MobilePosition,
        };
    }

    private HeroBrandViewModel? NormalizeHeroBrand(HeroBrandViewModel? heroBrand)
    {
        if (heroBrand is null)
            return null;

        var imageUrl = NormalizeOptionalUrl(heroBrand.ImageUrl)
            ?? _streamingServiceLogos.ResolveLogoPath(heroBrand.Label);

        return new HeroBrandViewModel
        {
            Label = heroBrand.Label,
            ImageUrl = imageUrl,
        };
    }

    private CreditGroupViewModel NormalizeCreditGroup(CreditGroupViewModel group) => new()
    {
        Title = group.Title,
        GroupType = group.GroupType,
        Credits = group.Credits.Select(NormalizeCredit).ToList(),
    };

    private EntityCreditViewModel NormalizeCredit(EntityCreditViewModel credit) => new()
    {
        EntityId = credit.EntityId,
        EntityType = credit.EntityType,
        DisplayName = credit.DisplayName,
        ImageUrl = NormalizeOptionalUrl(credit.ImageUrl),
        FallbackInitials = credit.FallbackInitials,
        PrimaryRole = credit.PrimaryRole,
        SecondaryRole = credit.SecondaryRole,
        CharacterName = credit.CharacterName,
        CharacterEntityId = credit.CharacterEntityId,
        CharacterImageUrl = NormalizeOptionalUrl(credit.CharacterImageUrl),
        SortOrder = credit.SortOrder,
        IsPrimary = credit.IsPrimary,
        IsCanonical = credit.IsCanonical,
        SourceName = credit.SourceName,
        SourceId = credit.SourceId,
    };

    private SeriesPlacementViewModel? NormalizeSeriesPlacement(SeriesPlacementViewModel? placement)
        => placement is null
            ? null
            : new SeriesPlacementViewModel
            {
                SeriesId = placement.SeriesId,
                SeriesTitle = placement.SeriesTitle,
                UniverseId = placement.UniverseId,
                UniverseTitle = placement.UniverseTitle,
                PositionNumber = placement.PositionNumber,
                TotalKnownItems = placement.TotalKnownItems,
                PositionLabel = placement.PositionLabel,
                OrderingType = placement.OrderingType,
                SelectedSeriesId = placement.SelectedSeriesId,
                CanChooseSeries = placement.CanChooseSeries,
                CanSetDefaultSeries = placement.CanSetDefaultSeries,
                AvailableSeries = placement.AvailableSeries,
                PreviousItem = NormalizeSeriesItem(placement.PreviousItem),
                CurrentItem = NormalizeSeriesItem(placement.CurrentItem) ?? new SeriesItemViewModel(),
                NextItem = NormalizeSeriesItem(placement.NextItem),
                OrderedItems = placement.OrderedItems.Select(NormalizeSeriesItem).OfType<SeriesItemViewModel>().ToList(),
            };

    private SeriesItemViewModel? NormalizeSeriesItem(SeriesItemViewModel? item)
        => item is null
            ? null
            : new SeriesItemViewModel
            {
                Id = item.Id,
                EntityType = item.EntityType,
                Title = item.Title,
                ArtworkUrl = NormalizeOptionalUrl(item.ArtworkUrl),
                PositionNumber = item.PositionNumber,
                PositionLabel = item.PositionLabel,
                IsCurrent = item.IsCurrent,
                IsOwned = item.IsOwned,
                ProgressState = item.ProgressState,
            };

    private string? NormalizeOptionalUrl(string? value)
        => string.IsNullOrWhiteSpace(value) ? value : AbsoluteUrl(value);

    private ProfileViewModel NormalizeProfile(ProfileViewModel profile) =>
        profile with
        {
            AvatarImageUrl = NormalizeOptionalUrl(profile.AvatarImageUrl),
        };

    /// <summary>
    /// Converts relative /stream/… paths stored in canonical values to absolute
    /// Engine URLs so Dashboard components can use them directly as &lt;img src&gt;.
    /// </summary>
    private string AbsoluteUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (_http.BaseAddress is { } baseAddr)
            return new Uri(baseAddr, value.StartsWith('/') ? value : $"/{value}").ToString();

        return value;
    }

    private static string GetImageContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg",
        };

    private string? ResolvePersonHeadshotUrl(PersonRaw person) =>
        person.HasLocalHeadshot || !string.IsNullOrWhiteSpace(person.HeadshotUrl)
            ? AbsoluteUrl($"/persons/{person.Id}/headshot")
            : null;

    private static void AddQuery(ICollection<string> query, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
    }

    private string NormalizeCanonicalValue(string key, string value) =>
        IsArtworkCanonicalKey(key)
            ? AbsoluteUrl(value)
            : value;

    private static bool IsArtworkCanonicalKey(string? key)
    {
        var normalized = key?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        return normalized switch
        {
            "cover" or "cover_url" or
            "square" or "square_url" or
            "background" or "background_url" or
            "banner" or "banner_url" or
            "hero" or "hero_url" or
            "logo" or "logo_url" or
            "artist_photo_url" or "headshot_url" or
            "season_poster" or "season_poster_url" or
            "season_thumb" or "season_thumb_url" or
            "episode_still" or "episode_still_url" or
            "character_portrait" or "character_portrait_url" or
            "disc_art_url" or "clear_art_url" => true,
            _ when normalized.EndsWith("_url_s", StringComparison.Ordinal)
                || normalized.EndsWith("_url_m", StringComparison.Ordinal)
                || normalized.EndsWith("_url_l", StringComparison.Ordinal) => true,
            _ => false,
        };
    }

    private WorkViewModel MapLibraryWork(LibraryWorkRaw work)
    {
        var canonicalValues = (work.CanonicalValues ?? new())
            .Select(kv => new CanonicalValueViewModel
            {
                Key = kv.Key,
                Value = NormalizeCanonicalValue(kv.Key, kv.Value),
            })
            .ToList();

        return new WorkViewModel
        {
            Id = work.Id,
            CollectionId = work.CollectionId,
            RootWorkId = work.RootWorkId,
            AssetId = work.AssetId,
            MediaType = work.MediaType ?? "Unknown",
            WorkKind = work.WorkKind,
            Ordinal = work.Ordinal,
            CreatedAt = ParseDateTimeOffset(work.CreatedAt),
            ResolvedCoverUrl = work.CoverUrl is not null ? AbsoluteUrl(work.CoverUrl) : SelectCanonicalUrl(canonicalValues, "cover_url", "cover"),
            ResolvedBackgroundUrl = work.BackgroundUrl is not null ? AbsoluteUrl(work.BackgroundUrl) : SelectCanonicalUrl(canonicalValues, "background_url", "background"),
            ResolvedBannerUrl = work.BannerUrl is not null ? AbsoluteUrl(work.BannerUrl) : SelectCanonicalUrl(canonicalValues, "banner_url", "banner"),
            ResolvedHeroUrl = null,
            ResolvedLogoUrl = work.LogoUrl is not null ? AbsoluteUrl(work.LogoUrl) : SelectCanonicalUrl(canonicalValues, "logo_url", "logo"),
            CanonicalValues = canonicalValues,
        };
    }

    private WorkViewModel MapWork(WorkRaw work)
    {
        var canonicalValues = work.CanonicalValues.Select(cv => new CanonicalValueViewModel
        {
            Key = cv.Key,
            Value = NormalizeCanonicalValue(cv.Key, cv.Value),
            LastScoredAt = cv.LastScoredAt,
        }).ToList();

        return new WorkViewModel
        {
            Id = work.Id,
            CollectionId = work.CollectionId,
            MediaType = work.MediaType,
            Ordinal = work.Ordinal,
            ResolvedCoverUrl = SelectCanonicalUrl(canonicalValues, "cover_url", "cover"),
            ResolvedBackgroundUrl = SelectCanonicalUrl(canonicalValues, "background_url", "background"),
            ResolvedBannerUrl = SelectCanonicalUrl(canonicalValues, "banner_url", "banner"),
            ResolvedHeroUrl = null,
            ResolvedLogoUrl = SelectCanonicalUrl(canonicalValues, "logo_url", "logo"),
            CanonicalValues = canonicalValues,
        };
    }

    private static DateTimeOffset ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;

    private string? SelectCanonicalUrl(IEnumerable<CanonicalValueViewModel> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            var match = values.FirstOrDefault(value => value.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(match))
                return AbsoluteUrl(match);
        }

        return null;
    }

    private CollectionViewModel MapCollection(CollectionRaw h) => CollectionViewModel.FromApiDto(
        h.Id,
        h.UniverseId,
        h.CreatedAt,
        h.Works.Select(MapWork),
        displayName:   h.DisplayName,
        parentCollectionId:   h.ParentCollectionId,
        parentCollectionName: h.ParentCollectionName,
        childCollectionCount: h.ChildCollectionCount);

    private static GroupMemberView MapGroupMember(GroupMemberRaw groupMember) =>
        new(groupMember.Id, groupMember.Name ?? string.Empty, groupMember.DateRange);

    private CollectionViewModel MapParentCollection(ParentCollectionRaw h) => CollectionViewModel.FromParentCollection(
        h.Id,
        h.UniverseId,
        h.CreatedAt,
        displayName:   h.DisplayName,
        description:   h.Description,
        wikidataQid:   h.WikidataQid,
        childCollectionCount: h.ChildCollectionCount,
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
        var endpoint = $"GET /read/{assetId}/metadata";
        try
        {
            var response = await _http.GetAsync($"read/{assetId}/metadata", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var metadata = await response.Content.ReadFromJsonAsync<EpubBookMetadataDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return metadata;
        }
        catch (Exception ex) { RecordExceptionFailure(endpoint, ex); return null; }
    }

    public async Task<List<EpubTocEntryDto>> GetTableOfContentsAsync(Guid assetId, CancellationToken ct = default)
    {
        var endpoint = $"GET /read/{assetId}/toc";
        try
        {
            var response = await _http.GetAsync($"read/{assetId}/toc", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return [];
            }

            var toc = await response.Content.ReadFromJsonAsync<List<EpubTocEntryDto>>(cancellationToken: ct) ?? [];
            ClearFailure(endpoint);
            return toc;
        }
        catch (Exception ex) { RecordExceptionFailure(endpoint, ex); return []; }
    }

    public async Task<EpubChapterContentDto?> GetChapterContentAsync(Guid assetId, int chapterIndex, CancellationToken ct = default)
    {
        var endpoint = $"GET /read/{assetId}/chapter/{chapterIndex}";
        try
        {
            var response = await _http.GetAsync($"read/{assetId}/chapter/{chapterIndex}", ct);
            if (!response.IsSuccessStatusCode)
            {
                await RecordHttpFailureAsync(endpoint, response, ct);
                return null;
            }

            var chapter = await response.Content.ReadFromJsonAsync<EpubChapterContentDto>(cancellationToken: ct);
            ClearFailure(endpoint);
            return chapter;
        }
        catch (Exception ex) { RecordExceptionFailure(endpoint, ex); return null; }
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

    private sealed record CollectionRaw(
        [property: JsonPropertyName("id")]              Guid           Id,
        [property: JsonPropertyName("universe_id")]     Guid?          UniverseId,
        [property: JsonPropertyName("display_name")]    string?        DisplayName,
        [property: JsonPropertyName("created_at")]      DateTimeOffset CreatedAt,
        [property: JsonPropertyName("works")]           List<WorkRaw>  Works,
        [property: JsonPropertyName("parent_collection_id")]   Guid?          ParentCollectionId   = null,
        [property: JsonPropertyName("parent_collection_name")] string?        ParentCollectionName = null,
        [property: JsonPropertyName("child_collection_count")] int            ChildCollectionCount = 0);

    private sealed record ParentCollectionRaw(
        [property: JsonPropertyName("id")]               Guid           Id,
        [property: JsonPropertyName("universe_id")]      Guid?          UniverseId,
        [property: JsonPropertyName("display_name")]     string?        DisplayName,
        [property: JsonPropertyName("description")]      string?        Description,
        [property: JsonPropertyName("wikidata_qid")]     string?        WikidataQid,
        [property: JsonPropertyName("universe_status")]  string?        UniverseStatus,
        [property: JsonPropertyName("created_at")]       DateTimeOffset CreatedAt,
        [property: JsonPropertyName("child_collection_count")]  int            ChildCollectionCount  = 0,
        [property: JsonPropertyName("media_types")]      string?        MediaTypes     = null,
        [property: JsonPropertyName("total_works")]      int            TotalWorks     = 0);

    private sealed record WorkRaw(
        [property: JsonPropertyName("id")]               Guid                      Id,
        [property: JsonPropertyName("collection_id")]           Guid?                     CollectionId,
        [property: JsonPropertyName("media_type")]       string                    MediaType,
        [property: JsonPropertyName("ordinal")]          int?                      Ordinal,
        [property: JsonPropertyName("canonical_values")] List<CanonicalValueRaw>   CanonicalValues);

    private sealed record CanonicalValueRaw(
        [property: JsonPropertyName("key")]            string        Key,
        [property: JsonPropertyName("value")]          string        Value,
        [property: JsonPropertyName("last_scored_at")] DateTimeOffset LastScoredAt);

    private sealed class LibraryWorkRaw
    {
        [JsonPropertyName("id")]              public Guid Id { get; set; }
        [JsonPropertyName("collectionId")]    public Guid? CollectionId { get; set; }
        [JsonPropertyName("rootWorkId")]      public Guid? RootWorkId { get; set; }
        [JsonPropertyName("mediaType")]       public string? MediaType { get; set; }
        [JsonPropertyName("workKind")]        public string? WorkKind { get; set; }
        [JsonPropertyName("ordinal")]         public int? Ordinal { get; set; }
        [JsonPropertyName("wikidataQid")]     public string? WikidataQid { get; set; }
        [JsonPropertyName("assetId")]         public Guid? AssetId { get; set; }
        [JsonPropertyName("createdAt")]       public string? CreatedAt { get; set; }
        [JsonPropertyName("coverUrl")]        public string? CoverUrl { get; set; }
        [JsonPropertyName("backgroundUrl")]   public string? BackgroundUrl { get; set; }
        [JsonPropertyName("bannerUrl")]       public string? BannerUrl { get; set; }
        [JsonPropertyName("heroUrl")]         public string? HeroUrl { get; set; }
        [JsonPropertyName("logoUrl")]         public string? LogoUrl { get; set; }
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
        [property: JsonPropertyName("collection_id")]           Guid?   CollectionId,
        [property: JsonPropertyName("title")]            string  Title,
        [property: JsonPropertyName("author")]           string? Author,
        [property: JsonPropertyName("media_type")]       string  MediaType,
        [property: JsonPropertyName("collection_display_name")] string  CollectionDisplayName);

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
        [property: JsonPropertyName("collectionId")]              Guid?                         CollectionId,
        [property: JsonPropertyName("title")]              string?                       Title,
        [property: JsonPropertyName("author")]             string?                       Author,
        [property: JsonPropertyName("coverUrl")]           string?                       CoverUrl,
        [property: JsonPropertyName("backgroundUrl")]      string?                       BackgroundUrl,
        [property: JsonPropertyName("bannerUrl")]          string?                       BannerUrl,
        [property: JsonPropertyName("logoUrl")]            string?                       LogoUrl,
        [property: JsonPropertyName("coverWidthPx")]       int?                          CoverWidthPx,
        [property: JsonPropertyName("coverHeightPx")]      int?                          CoverHeightPx,
        [property: JsonPropertyName("backgroundWidthPx")]  int?                          BackgroundWidthPx,
        [property: JsonPropertyName("backgroundHeightPx")] int?                          BackgroundHeightPx,
        [property: JsonPropertyName("bannerWidthPx")]      int?                          BannerWidthPx,
        [property: JsonPropertyName("bannerHeightPx")]     int?                          BannerHeightPx,
        [property: JsonPropertyName("narrator")]           string?                       Narrator,
        [property: JsonPropertyName("series")]             string?                       Series,
        [property: JsonPropertyName("seriesPosition")]     string?                       SeriesPosition,
        [property: JsonPropertyName("description")]        string?                       Description,
        [property: JsonPropertyName("mediaType")]          string?                       MediaType,
        [property: JsonPropertyName("progressPct")]        double                        ProgressPct,
        [property: JsonPropertyName("lastAccessed")]       DateTimeOffset                LastAccessed,
        [property: JsonPropertyName("collectionDisplayName")]     string?                       CollectionDisplayName,
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

    private sealed record RelatedCollectionsRaw(
        [property: JsonPropertyName("section_title")] string       SectionTitle,
        [property: JsonPropertyName("reason")]        string       Reason,
        [property: JsonPropertyName("collections")]          List<CollectionRaw> Collections);

    private sealed record ParentCollectionResponseRaw(
        [property: JsonPropertyName("parentCollection")] CollectionRaw? ParentCollection);

    private sealed record PersonDetailRaw(
        [property: JsonPropertyName("id")]                 Guid            Id,
        [property: JsonPropertyName("name")]               string?         Name,
        [property: JsonPropertyName("roles")]              List<string>?   Roles,
        [property: JsonPropertyName("wikidata_qid")]       string?         WikidataQid,
        [property: JsonPropertyName("headshot_url")]       string?         HeadshotUrl,
        [property: JsonPropertyName("has_local_headshot")] bool            HasLocalHeadshot,
        [property: JsonPropertyName("biography")]          string?         Biography,
        [property: JsonPropertyName("occupation")]         string?         Occupation,
        [property: JsonPropertyName("date_of_birth")]      string?         DateOfBirth,
        [property: JsonPropertyName("date_of_death")]      string?         DateOfDeath,
        [property: JsonPropertyName("place_of_birth")]     string?         PlaceOfBirth,
        [property: JsonPropertyName("place_of_death")]     string?         PlaceOfDeath,
        [property: JsonPropertyName("nationality")]        string?         Nationality,
        [property: JsonPropertyName("instagram")]          string?         Instagram,
        [property: JsonPropertyName("twitter")]            string?         Twitter,
        [property: JsonPropertyName("tiktok")]             string?         TikTok,
        [property: JsonPropertyName("mastodon")]           string?         Mastodon,
        [property: JsonPropertyName("website")]            string?         Website,
        [property: JsonPropertyName("is_group")]           bool            IsGroup,
        [property: JsonPropertyName("group_members")]      List<GroupMemberRaw>? GroupMembers,
        [property: JsonPropertyName("member_of_groups")]   List<GroupMemberRaw>? MemberOfGroups,
        [property: JsonPropertyName("banner_url")]         string?         BannerUrl,
        [property: JsonPropertyName("background_url")]     string?         BackgroundUrl,
        [property: JsonPropertyName("logo_url")]           string?         LogoUrl,
        [property: JsonPropertyName("created_at")]         DateTimeOffset  CreatedAt,
        [property: JsonPropertyName("enriched_at")]        DateTimeOffset? EnrichedAt);

    private sealed record GroupMemberRaw(
        [property: JsonPropertyName("id")]         Guid    Id,
        [property: JsonPropertyName("name")]       string? Name,
        [property: JsonPropertyName("date_range")] string? DateRange);

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

    // ── Collection Group Detail (Vault drill-down sub-pages) ─────────────────────────

    public async Task<CollectionGroupDetailViewModel?> GetCollectionGroupDetailAsync(Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/{collectionId}/group-detail", ct);
            NormalizeCollectionGroupDetail(result);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/group-detail failed", collectionId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<CollectionGroupDetailViewModel?> GetArtistGroupDetailAsync(IEnumerable<Guid> collectionIds, CancellationToken ct = default)
    {
        try
        {
            var idsParam = string.Join(",", collectionIds);
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/artist-group-detail?collection_ids={idsParam}", ct);
            NormalizeCollectionGroupDetail(result);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/artist-group-detail failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<CollectionGroupDetailViewModel?> GetArtistDetailByNameAsync(string artistName, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/artist-detail-by-name?artistName={Uri.EscapeDataString(artistName)}", ct);
            NormalizeCollectionGroupDetail(result);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/artist-detail-by-name failed for {ArtistName}", artistName);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<CollectionGroupDetailViewModel?> GetSystemViewGroupDetailAsync(string groupField, string groupValue, string? mediaType = null, string? artistName = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/collections/system-view-detail?groupField={Uri.EscapeDataString(groupField)}&groupValue={Uri.EscapeDataString(groupValue)}";
            if (!string.IsNullOrWhiteSpace(mediaType))
                url += $"&mediaType={Uri.EscapeDataString(mediaType)}";
            if (!string.IsNullOrWhiteSpace(artistName))
                url += $"&artistName={Uri.EscapeDataString(artistName)}";
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(url, ct);
            NormalizeCollectionGroupDetail(result);
            return result;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/system-view-detail failed for {GroupField}={GroupValue}", groupField, groupValue);
            LastError = ex.Message;
            return null;
        }
    }

    // ── Managed Collections (Vault Collections tab) ────────────────────────────────────────

    private static string AppendCollectionProfileQuery(string url, Guid? profileId)
    {
        if (!profileId.HasValue)
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}profileId={profileId.Value:D}";
    }

    public async Task<List<ManagedCollectionViewModel>> GetManagedCollectionsAsync(Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery("/collections/managed", profileId);
            var collections = await _http.GetFromJsonAsync<List<ManagedCollectionViewModel>>(url, ct) ?? [];
            foreach (var collection in collections)
            {
                if (collection.SquareArtworkUrl is not null)
                    collection.SquareArtworkUrl = AbsoluteUrl(collection.SquareArtworkUrl);
            }

            return collections;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/managed failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<Dictionary<string, int>> GetManagedCollectionCountsAsync(Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery("/collections/managed/counts", profileId);
            return await _http.GetFromJsonAsync<Dictionary<string, int>>(url, ct) ?? new();
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/managed/counts failed");
            LastError = ex.Message;
            return new();
        }
    }

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

    public async Task<bool> DeleteCollectionSquareArtworkAsync(Guid collectionId, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/square-artwork", profileId);
            var response = await _http.DeleteAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /collections/{CollectionId}/square-artwork failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<bool> DeleteCollectionAsync(Guid collectionId, Guid? profileId = null, CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}", profileId);
            var response = await _http.DeleteAsync(url, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DELETE /collections/{CollectionId} failed", collectionId);
            LastError = ex.Message;
            return false;
        }
    }

    public async Task<List<CollectionResolvedItemViewModel>> ResolveCollectionAsync(Guid collectionId, int? limit = null, CancellationToken ct = default)
    {
        try
        {
            var url = limit.HasValue ? $"/collections/resolve/{collectionId}?limit={limit}" : $"/collections/resolve/{collectionId}";
            return await _http.GetFromJsonAsync<List<CollectionResolvedItemViewModel>>(url, ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/resolve/{CollectionId} failed", collectionId);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<List<CollectionResolvedItemViewModel>> ResolveCollectionByNameAsync(string name, int? limit = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"/collections/resolve/by-name?name={Uri.EscapeDataString(name)}";
            if (limit.HasValue) url += $"&limit={limit}";
            return await _http.GetFromJsonAsync<List<CollectionResolvedItemViewModel>>(url, ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/resolve/by-name failed for collection '{Name}'", name);
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
                $"/library/universes/{Uri.EscapeDataString(universeQid)}/characters", ct);
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
            _logger.LogWarning(ex, "GET /library/universes/{Qid}/characters failed", universeQid);
            return [];
        }
    }

    public async Task<IReadOnlyList<CharacterRoleDto>> GetPersonCharacterRolesAsync(Guid personId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<CharacterRoleRaw>>(
                $"/library/persons/{personId}/character-roles", ct);
            if (raw is null) return [];
            return raw.Select(r => new CharacterRoleDto
            {
                FictionalEntityId = r.FictionalEntityId,
                CharacterName     = r.CharacterName,
                PortraitUrl       = r.PortraitUrl is not null ? AbsoluteUrl(r.PortraitUrl) : null,
                WorkId            = r.WorkId,
                WorkQid           = r.WorkQid,
                WorkTitle         = r.WorkTitle,
                CollectionId      = r.CollectionId,
                MediaType         = r.MediaType,
                IsDefault         = r.IsDefault,
                UniverseQid       = r.UniverseQid,
                UniverseLabel     = r.UniverseLabel,
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/persons/{PersonId}/character-roles failed", personId);
            return [];
        }
    }

    public async Task SetDefaultPortraitAsync(Guid fictionalEntityId, Guid portraitId, CancellationToken ct = default)
    {
        try
        {
            await _http.PutAsJsonAsync(
                $"/library/characters/{fictionalEntityId}/portraits/{portraitId}/default",
                new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /library/characters/{EntityId}/portraits/{PortraitId}/default failed",
                fictionalEntityId, portraitId);
        }
    }

    public async Task<IReadOnlyList<EntityAssetDto>> GetEntityAssetsAsync(string entityId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<List<EntityAssetRaw>>(
                $"/library/assets/{Uri.EscapeDataString(entityId)}", ct);
            if (raw is null) return [];
            return raw.Select(r => new EntityAssetDto
            {
                Id             = r.Id,
                EntityId       = r.EntityId ?? entityId,
                AssetType      = r.AssetType ?? string.Empty,
                ImageUrl       = r.ImageUrl is not null ? AbsoluteUrl(r.ImageUrl) : null,
                IsPreferred    = r.IsPreferred,
                SourceProvider = r.SourceProvider,
            }).ToList();
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/assets/{EntityId} failed", entityId);
            return [];
        }
    }

    public async Task<List<CollectionGroupPersonViewModel>> GetWorkCastAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            var cast = await _http.GetFromJsonAsync<List<CollectionGroupPersonViewModel>>(
                $"/works/{workId}/cast", ct);

            NormalizeCastCredits(cast);
            return cast ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /works/{WorkId}/cast failed", workId);
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<ArtworkEditorDto?> GetArtworkAsync(Guid entityId, CancellationToken ct = default)
    {
        try
        {
            var raw = await _http.GetFromJsonAsync<ArtworkEditorRaw>($"/metadata/{entityId}/artwork", ct);
            if (raw is null)
                return null;

            return new ArtworkEditorDto
            {
                EntityId = raw.EntityId,
                Slots = raw.Slots.Select(slot => new ArtworkSlotDto
                {
                    AssetType = slot.AssetType ?? string.Empty,
                    Variants = slot.Variants.Select(variant => new ArtworkVariantDto
                    {
                        Id = variant.Id,
                        AssetType = variant.AssetType ?? slot.AssetType ?? string.Empty,
                        ImageUrl = variant.ImageUrl is not null ? AbsoluteUrl(variant.ImageUrl) : null,
                        IsPreferred = variant.IsPreferred,
                        Origin = string.IsNullOrWhiteSpace(variant.Origin) ? "Stored" : variant.Origin,
                        ProviderName = variant.ProviderName,
                        CanDelete = variant.CanDelete,
                        CreatedAt = variant.CreatedAt,
                    }).ToList(),
                }).ToList(),
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/{EntityId}/artwork failed", entityId);
            return null;
        }
    }

    public async Task<ArtworkEditorDto?> GetScopeArtworkAsync(Guid entityId, string scopeId, CancellationToken ct = default)
    {
        try
        {
            var encodedScope = Uri.EscapeDataString(scopeId);
            var raw = await _http.GetFromJsonAsync<ArtworkEditorRaw>($"/metadata/{entityId}/artwork/{encodedScope}", ct);
            if (raw is null)
                return null;

            return new ArtworkEditorDto
            {
                EntityId = raw.EntityId,
                Slots = raw.Slots.Select(slot => new ArtworkSlotDto
                {
                    AssetType = slot.AssetType ?? string.Empty,
                    Variants = slot.Variants.Select(variant => new ArtworkVariantDto
                    {
                        Id = variant.Id,
                        AssetType = variant.AssetType ?? slot.AssetType ?? string.Empty,
                        ImageUrl = variant.ImageUrl is not null ? AbsoluteUrl(variant.ImageUrl) : null,
                        IsPreferred = variant.IsPreferred,
                        Origin = string.IsNullOrWhiteSpace(variant.Origin) ? "Stored" : variant.Origin,
                        ProviderName = variant.ProviderName,
                        CanDelete = variant.CanDelete,
                        CreatedAt = variant.CreatedAt,
                    }).ToList(),
                }).ToList(),
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /metadata/{EntityId}/artwork/{ScopeId} failed", entityId, scopeId);
            return null;
        }
    }

    public async Task TriggerUniverseEnrichmentAsync(CancellationToken ct = default)
    {
        try
        {
            await _http.PostAsJsonAsync("/library/enrichment/universe/trigger", new { }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/enrichment/universe/trigger failed");
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
        [JsonPropertyName("work_id")]             public Guid?   WorkId            { get; set; }
        [JsonPropertyName("work_qid")]            public string? WorkQid           { get; set; }
        [JsonPropertyName("work_title")]          public string? WorkTitle         { get; set; }
        [JsonPropertyName("collection_id")]       public Guid?   CollectionId      { get; set; }
        [JsonPropertyName("media_type")]          public string? MediaType         { get; set; }
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

    private sealed class ArtworkEditorRaw
    {
        [JsonPropertyName("entity_id")] public Guid EntityId { get; set; }
        [JsonPropertyName("slots")] public List<ArtworkSlotRaw> Slots { get; set; } = [];
    }

    private sealed class ArtworkSlotRaw
    {
        [JsonPropertyName("asset_type")] public string? AssetType { get; set; }
        [JsonPropertyName("variants")] public List<ArtworkVariantRaw> Variants { get; set; } = [];
    }

    private sealed class ArtworkVariantRaw
    {
        [JsonPropertyName("id")] public Guid Id { get; set; }
        [JsonPropertyName("asset_type")] public string? AssetType { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
        [JsonPropertyName("is_preferred")] public bool IsPreferred { get; set; }
        [JsonPropertyName("origin")] public string? Origin { get; set; }
        [JsonPropertyName("provider_name")] public string? ProviderName { get; set; }
        [JsonPropertyName("can_delete")] public bool CanDelete { get; set; }
        [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    }

    // ── Vault Preferences ─────────────────────────────────────────────────────

    public async Task<LibraryPreferencesSettings?> GetLibraryPreferencesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<LibraryPreferencesSettings>("settings/ui/library-preferences");
        }
        catch { return null; }
    }

    public async Task SaveLibraryPreferencesAsync(LibraryPreferencesSettings settings)
    {
        try
        {
            await _http.PutAsJsonAsync("settings/ui/library-preferences", settings);
        }
        catch { /* swallow — preferences are non-critical */ }
    }

    // ── Vault Overview ──

    public async Task<LibraryOverviewViewModel?> GetLibraryOverviewAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<LibraryOverviewViewModel>("library/overview", ct);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/overview failed");
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<LibraryBatchEditResultViewModel?> BatchEditAsync(
        List<Guid> entityIds, Dictionary<string, string> fieldChanges, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                entity_ids = entityIds,
                field_changes = fieldChanges.Select(kv => new { key = kv.Key, value = kv.Value }).ToList(),
            };
            var response = await _http.PostAsJsonAsync("library/batch-edit", body, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<LibraryBatchEditResultViewModel>(ct);
        }
        catch
        {
            return null;
        }
    }

    // ── Universe Alignment ──

    public async Task<List<UniverseCandidateViewModel>> GetUniverseCandidatesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UniverseCandidateViewModel>>("library/universe-candidates", ct) ?? [];
        }
        catch { return []; }
    }

    public async Task<bool> AcceptUniverseCandidateAsync(Guid workId, string targetCollectionQid, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"library/universe-candidates/{workId}/accept",
                new { target_collection_qid = targetCollectionQid }, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> RejectUniverseCandidateAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"library/universe-candidates/{workId}/reject", new { }, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<int> BatchAcceptUniverseCandidatesAsync(List<Guid> workIds, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("library/universe-candidates/batch-accept",
                new { work_ids = workIds }, ct);
            if (!response.IsSuccessStatusCode) return 0;
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return result.TryGetProperty("accepted_count", out var count) ? count.GetInt32() : 0;
        }
        catch { return 0; }
    }

    public async Task<List<UnlinkedWorkViewModel>> GetUniverseUnlinkedAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UnlinkedWorkViewModel>>("library/universe-unlinked", ct) ?? [];
        }
        catch { return []; }
    }

    public async Task<bool> ManualUniverseAssignAsync(Guid workId, Guid collectionId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync("library/universe-assign",
                new { work_id = workId, collection_id = collectionId }, ct);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }
}



