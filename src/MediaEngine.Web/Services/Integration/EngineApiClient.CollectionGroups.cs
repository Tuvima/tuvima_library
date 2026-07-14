using System.Net.Http.Json;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Collection, artist, and system-view drill-down operations.
/// </summary>
public sealed partial class EngineApiClient
{
    public async Task<CollectionGroupDetailViewModel?> GetCollectionGroupDetailAsync(
        Guid collectionId,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/{collectionId}/group-detail",
                ct);
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

    public async Task<CollectionGroupDetailViewModel?> GetArtistGroupDetailAsync(
        IEnumerable<Guid> collectionIds,
        CancellationToken ct = default)
    {
        try
        {
            var idsParam = string.Join(",", collectionIds);
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/artist-group-detail?collection_ids={idsParam}",
                ct);
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

    public async Task<CollectionGroupDetailViewModel?> GetArtistDetailByNameAsync(
        string artistName,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _http.GetFromJsonAsync<CollectionGroupDetailViewModel>(
                $"/collections/artist-detail-by-name?artistName={Uri.EscapeDataString(artistName)}",
                ct);
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

    public async Task<CollectionGroupDetailViewModel?> GetSystemViewGroupDetailAsync(
        string groupField,
        string groupValue,
        string? mediaType = null,
        string? artistName = null,
        CancellationToken ct = default)
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
            _logger.LogWarning(
                ex,
                "GET /collections/system-view-detail failed for {GroupField}={GroupValue}",
                groupField,
                groupValue);
            LastError = ex.Message;
            return null;
        }
    }
}
