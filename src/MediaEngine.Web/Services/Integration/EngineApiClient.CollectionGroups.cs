using System.Net.Http.Json;
using MediaEngine.Contracts.Collections;
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
            var contract = await _http.GetFromJsonAsync<CollectionGroupDetailDto>(
                $"/collections/{collectionId}/group-detail",
                ct);
            var result = contract is null ? null : CollectionGroupDetailViewModel.FromContract(contract);
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

    public async Task<CollectionGroupDetailViewModel?> GetArtistDetailByNameAsync(
        string artistName,
        CancellationToken ct = default)
    {
        try
        {
            var contract = await _http.GetFromJsonAsync<CollectionGroupDetailDto>(
                $"/collections/artist-detail-by-name?artistName={Uri.EscapeDataString(artistName)}",
                ct);
            var result = contract is null ? null : CollectionGroupDetailViewModel.FromContract(contract);
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

            var contract = await _http.GetFromJsonAsync<CollectionGroupDetailDto>(url, ct);
            var result = contract is null ? null : CollectionGroupDetailViewModel.FromContract(contract);
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
