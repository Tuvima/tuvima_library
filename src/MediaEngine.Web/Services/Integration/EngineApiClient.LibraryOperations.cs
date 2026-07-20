using System.Net.Http.Json;
using System.Text.Json;
using MediaEngine.Contracts.Settings;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Library preferences, batch editing, overview, and universe-alignment operations.
/// </summary>
public sealed partial class EngineApiClient
{
    public async Task<LibraryPreferencesSettings?> GetLibraryPreferencesAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<LibraryPreferencesSettings>("settings/ui/library-preferences");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /settings/ui/library-preferences failed");
            return null;
        }
    }

    public async Task SaveLibraryPreferencesAsync(LibraryPreferencesSettings settings)
    {
        try
        {
            await _http.PutAsJsonAsync("settings/ui/library-preferences", settings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PUT /settings/ui/library-preferences failed");
        }
    }

    public async Task<SeriesMissingItemPreferenceDto?> GetSeriesMissingItemPreferenceAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        CancellationToken ct = default)
    {
        var endpoint = BuildSeriesMissingItemPreferenceEndpoint(profileId, mediaType, containerKey);
        try
        {
            return await _http.GetFromJsonAsync<SeriesMissingItemPreferenceDto>(endpoint, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "GET profile series missing-item preference failed for {ProfileId} {MediaType} {ContainerKey}",
                profileId,
                mediaType,
                containerKey);
            return null;
        }
    }

    public async Task<SeriesMissingItemPreferenceDto?> SaveSeriesMissingItemPreferenceAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        bool showMissing,
        CancellationToken ct = default)
    {
        var endpoint = $"profiles/{profileId:D}/sequence-preferences/missing-items";
        try
        {
            using var response = await _http.PutAsJsonAsync(
                endpoint,
                new SaveSeriesMissingItemPreferenceRequest
                {
                    MediaType = mediaType,
                    ContainerKey = containerKey,
                    ShowMissing = showMissing,
                },
                ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SeriesMissingItemPreferenceDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PUT profile series missing-item preference failed for {ProfileId} {MediaType} {ContainerKey}",
                profileId,
                mediaType,
                containerKey);
            return null;
        }
    }

    public async Task<SeriesMissingItemPreferenceDto?> ResetSeriesMissingItemPreferenceAsync(
        Guid profileId,
        string mediaType,
        string containerKey,
        CancellationToken ct = default)
    {
        var endpoint = BuildSeriesMissingItemPreferenceEndpoint(profileId, mediaType, containerKey);
        try
        {
            using var response = await _http.DeleteAsync(endpoint, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SeriesMissingItemPreferenceDto>(cancellationToken: ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "DELETE profile series missing-item preference failed for {ProfileId} {MediaType} {ContainerKey}",
                profileId,
                mediaType,
                containerKey);
            return null;
        }
    }

    private static string BuildSeriesMissingItemPreferenceEndpoint(
        Guid profileId,
        string mediaType,
        string containerKey) =>
        $"profiles/{profileId:D}/sequence-preferences/missing-items" +
        $"?mediaType={Uri.EscapeDataString(mediaType)}" +
        $"&containerKey={Uri.EscapeDataString(containerKey)}";

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
        List<Guid> entityIds,
        Dictionary<string, string> fieldChanges,
        CancellationToken ct = default)
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
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/batch-edit failed");
            return null;
        }
    }

    public async Task<List<UniverseCandidateViewModel>> GetUniverseCandidatesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UniverseCandidateViewModel>>("library/universe-candidates", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/universe-candidates failed");
            return [];
        }
    }

    public async Task<bool> AcceptUniverseCandidateAsync(
        Guid workId,
        string targetCollectionQid,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"library/universe-candidates/{workId}/accept",
                new { target_collection_qid = targetCollectionQid },
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/universe-candidates/{WorkId}/accept failed", workId);
            return false;
        }
    }

    public async Task<bool> RejectUniverseCandidateAsync(Guid workId, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync($"library/universe-candidates/{workId}/reject", new { }, ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/universe-candidates/{WorkId}/reject failed", workId);
            return false;
        }
    }

    public async Task<int> BatchAcceptUniverseCandidatesAsync(List<Guid> workIds, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "library/universe-candidates/batch-accept",
                new { work_ids = workIds },
                ct);
            if (!response.IsSuccessStatusCode)
                return 0;

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return result.TryGetProperty("accepted_count", out var count) ? count.GetInt32() : 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/universe-candidates/batch-accept failed");
            return 0;
        }
    }

    public async Task<List<UnlinkedWorkViewModel>> GetUniverseUnlinkedAsync(CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<UnlinkedWorkViewModel>>("library/universe-unlinked", ct) ?? [];
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /library/universe-unlinked failed");
            return [];
        }
    }

    public async Task<bool> ManualUniverseAssignAsync(
        Guid workId,
        Guid collectionId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                "library/universe-assign",
                new { work_id = workId, collection_id = collectionId },
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "POST /library/universe-assign failed for work {WorkId}", workId);
            return false;
        }
    }
}
