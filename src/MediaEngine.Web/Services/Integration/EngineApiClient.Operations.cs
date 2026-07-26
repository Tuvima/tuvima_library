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
    // -- POST /ingestion/scan --------------------------------------------------

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
    // Migrated to the shared PostAsync<TReq,TRes> helper (stage 5B wave 2). Kept async because the
    // ScanRaw -> ScanResultViewModel projection has to run after the helper's awaited result.
    public async Task<ScanResultViewModel?> TriggerScanAsync(
        string? rootPath = null,
        CancellationToken ct = default)
    {
        var raw = await PostAsync<object, ScanRaw>("POST /ingestion/scan", "/ingestion/scan", new { root_path = rootPath }, ct: ct);
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

    // -- POST /ingestion/reconcile ---------------------------------------------

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

    // -- GET /ingestion/watch-folder --------------------------------------------

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

    // -- POST /ingestion/rescan ----------------------------------------------

    // Migrated to the shared PostAsync<TReq> bool-returning helper (stage 5B wave 2).
    public Task<bool> TriggerRescanAsync(
        string? rootPath = null,
        bool? includeSubdirectories = null,
        CancellationToken ct = default) =>
        PostAsync<object>(
            "POST /ingestion/rescan",
            "/ingestion/rescan",
            new { root_path = rootPath, include_subdirectories = includeSubdirectories },
            ct: ct);

    // -- /metadata/hydrate ------------------------------------------------------

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

    // -- /metadata/conflicts ----------------------------------------------------

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

    // -- POST /dev/seed-library -----------------------------------------

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

    public async Task<ItemEditorPreferencesDto?> GetItemEditorPreferencesAsync(
        Guid entityId, Guid profileId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.GetAsync($"/library/items/{entityId}/editor-preferences/{profileId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
                _logger.LogWarning("GET editor preferences for {EntityId}/{ProfileId} returned {Status}: {Detail}",
                    entityId, profileId, (int)response.StatusCode, detail);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ItemEditorPreferencesDto>(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET editor preferences for {EntityId}/{ProfileId} failed", entityId, profileId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<ItemEditorPreferencesSaveResultDto> SaveItemEditorPreferencesAsync(
        Guid entityId,
        Guid profileId,
        ItemEditorPreferencesRequestDto request,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PutAsJsonAsync(
                $"/library/items/{entityId}/editor-preferences/{profileId}", request, ct);
            var result = await response.Content.ReadFromJsonAsync<ItemEditorPreferencesDto>(cancellationToken: ct);
            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                return new ItemEditorPreferencesSaveResultDto(false, true, result, "Source metadata or profile preferences changed while you were editing.");

            if (!response.IsSuccessStatusCode)
            {
                var detail = result is null ? await response.Content.ReadAsStringAsync(ct) : null;
                LastError = $"HTTP {(int)response.StatusCode}: {detail}";
                _logger.LogWarning("PUT editor preferences for {EntityId}/{ProfileId} returned {Status}: {Detail}",
                    entityId, profileId, (int)response.StatusCode, detail);
                return new ItemEditorPreferencesSaveResultDto(false, false, result, detail ?? "Editor preferences could not be saved.");
            }

            return new ItemEditorPreferencesSaveResultDto(true, false, result, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT editor preferences for {EntityId}/{ProfileId} failed", entityId, profileId);
            LastError = ex.Message;
            return new ItemEditorPreferencesSaveResultDto(false, false, null, ex.Message);
        }
    }

    public async Task<DevHarnessRunResult?> RunDevHarnessAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query = null,
        CancellationToken ct = default)
    {
        var endpointPath = BuildEndpointPath(path, query);
        var endpoint = $"POST {endpointPath}";
        var started = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        try
        {
            using var response = await _http.PostAsync(endpointPath, null, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            sw.Stop();

            var result = new DevHarnessRunResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                endpoint,
                response.Content.Headers.ContentType?.MediaType,
                body,
                started,
                DateTimeOffset.UtcNow,
                sw.ElapsedMilliseconds);

            if (response.IsSuccessStatusCode)
            {
                ClearFailure(endpoint);
            }
            else
            {
                _failureState.RecordRawFailure(endpoint, (int)response.StatusCode, SummarizeResponseBody(body));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordExceptionFailure(endpoint, ex);

            return new DevHarnessRunResult(
                false,
                0,
                endpoint,
                null,
                ex.Message,
                started,
                DateTimeOffset.UtcNow,
                sw.ElapsedMilliseconds);
        }
    }

    // -- Pass 2 (Universe Lookup) ----------------------------------------------

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

    // -- Retag Sweep (Auto re-tag) ---------------------------------------------

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

    // -- Library items (/library/items) -------------------------------------------

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
    public async Task<IngestionOperationsSnapshotViewModel?> GetIngestionOperationsSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<IngestionOperationsSnapshotViewModel>(
                "ingestion/operations", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch ingestion operations snapshot");
            LastError = ex.Message;
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MediaOperationViewModel>> GetMediaOperationsAsync(
        string? queueName = null, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var safeLimit = Math.Clamp(limit <= 0 ? 100 : limit, 1, 500);
            var query = $"operations?limit={safeLimit}";
            if (!string.IsNullOrWhiteSpace(queueName))
                query += $"&queueName={Uri.EscapeDataString(queueName)}";

            var result = await _http.GetFromJsonAsync<List<MediaOperationViewModel>>(
                query, ct).ConfigureAwait(false);
            return result ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch durable media operations");
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<MediaOperationDetailViewModel?> GetMediaOperationAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<MediaOperationDetailViewModel>(
                $"operations/{id:D}", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch media operation {Id}", id);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetMediaOperationsSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<Dictionary<string, int>>(
                "operations/summary", ct).ConfigureAwait(false) ?? new();
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch media operation summary");
            return new();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RetryMediaOperationAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"operations/{id:D}/retry", new { }, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retry media operation {Id}", id);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CancelMediaOperationAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"operations/{id:D}/cancel", new { }, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cancel media operation {Id}", id);
            return false;
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
    public async Task<PagedResponse<IngestionBatchItemViewModel>?> GetIngestionBatchItemsAsync(
        Guid id, int offset = 0, int limit = 100, CancellationToken ct = default)
    {
        try
        {
            var safeOffset = Math.Max(0, offset);
            var safeLimit = Math.Clamp(limit <= 0 ? 100 : limit, 1, 500);
            return await _http.GetFromJsonAsync<PagedResponse<IngestionBatchItemViewModel>>(
                $"ingestion/batches/{id:D}/items?offset={safeOffset}&limit={safeLimit}", ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch batch items for {Id}", id);
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EntityCapabilityStateViewModel>> GetAssetCapabilitiesAsync(
        Guid id, CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<List<EntityCapabilityStateViewModel>>(
                $"assets/{id:D}/capabilities", ct).ConfigureAwait(false);
            return result ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch capability states for asset {Id}", id);
            return [];
        }
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetCapabilitySummaryAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<Dictionary<string, int>>(
                "capabilities/summary", ct).ConfigureAwait(false) ?? new();
        }
        catch (OperationCanceledException) { return new(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch capability summary");
            return new();
        }
    }

    private sealed class AttentionCountResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("count")]
        public int Count { get; set; }
    }

}
