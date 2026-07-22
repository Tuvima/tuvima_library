using System.Net.Http.Json;
using MediaEngine.Web.Models.ViewDTOs;

namespace MediaEngine.Web.Services.Integration;

/// <summary>
/// Managed-collection catalogue operations exposed through <see cref="IEngineApiClient"/>.
/// Kept as a feature-focused partial so the established client contract remains stable.
/// </summary>
public sealed partial class EngineApiClient
{
    private static string AppendCollectionProfileQuery(string url, Guid? profileId)
    {
        if (!profileId.HasValue)
            return url;

        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{url}{separator}profileId={profileId.Value:D}";
    }

    public async Task<List<ManagedCollectionViewModel>> GetManagedCollectionsAsync(
        Guid? profileId = null,
        CancellationToken ct = default)
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

    public async Task<List<CollectionManagementCatalogViewModel>> GetCollectionCatalogAsync(
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery("/collections/catalog", profileId);
            var collections = await _http.GetFromJsonAsync<List<CollectionManagementCatalogViewModel>>(url, ct) ?? [];
            foreach (var collection in collections)
            {
                NormalizeManagedCollectionArtwork(collection);
            }

            return collections;
        }
        catch (OperationCanceledException) { return []; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/catalog failed");
            LastError = ex.Message;
            return [];
        }
    }

    public async Task<CollectionManagementCatalogViewModel?> GetCollectionSummaryAsync(
        Guid collectionId,
        Guid? profileId = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = AppendCollectionProfileQuery($"/collections/{collectionId}/summary", profileId);
            var collection = await _http.GetFromJsonAsync<CollectionManagementCatalogViewModel>(url, ct);
            if (collection is not null)
                NormalizeManagedCollectionArtwork(collection);

            return collection;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GET /collections/{CollectionId}/summary failed", collectionId);
            LastError = ex.Message;
            return null;
        }
    }

    public async Task<Dictionary<string, int>> GetManagedCollectionCountsAsync(
        Guid? profileId = null,
        CancellationToken ct = default)
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

    private void NormalizeManagedCollectionArtwork(CollectionManagementCatalogViewModel collection)
    {
        if (collection.SquareArtworkUrl is not null)
            collection.SquareArtworkUrl = AbsoluteUrl(collection.SquareArtworkUrl);

        if (collection.Person?.HeadshotUrl is not null)
            collection.Person.HeadshotUrl = AbsoluteUrl(collection.Person.HeadshotUrl);

        foreach (var artworkItem in collection.ArtworkItems)
        {
            if (artworkItem.CoverUrl is not null)
                artworkItem.CoverUrl = AbsoluteUrl(artworkItem.CoverUrl);
        }
    }
}
