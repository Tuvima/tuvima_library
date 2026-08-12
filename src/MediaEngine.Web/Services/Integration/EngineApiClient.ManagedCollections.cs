using System.Net.Http.Json;
using MediaEngine.Contracts.Collections;
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
            var contracts = await _http.GetFromJsonAsync<List<ManagedCollectionDto>>(url, ct) ?? [];
            var collections = contracts.Select(ManagedCollectionViewModel.FromContract).ToList();
            foreach (var collection in collections)
            {
                if (collection.CoverArtworkUrl is not null)
                    collection.CoverArtworkUrl = AbsoluteUrl(collection.CoverArtworkUrl);
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
            var contracts = await _http.GetFromJsonAsync<List<CollectionManagementCatalogDto>>(url, ct) ?? [];
            var collections = contracts.Select(CollectionManagementCatalogViewModel.FromContract).ToList();
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
            var contract = await _http.GetFromJsonAsync<CollectionManagementCatalogDto>(url, ct);
            var collection = contract is null ? null : CollectionManagementCatalogViewModel.FromContract(contract);
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

    private void NormalizeManagedCollectionArtwork(CollectionManagementCatalogViewModel collection)
    {
        if (collection.CoverArtworkUrl is not null)
            collection.CoverArtworkUrl = AbsoluteUrl(collection.CoverArtworkUrl);

        if (collection.Person?.HeadshotUrl is not null)
            collection.Person.HeadshotUrl = AbsoluteUrl(collection.Person.HeadshotUrl);

        foreach (var artworkItem in collection.ArtworkItems)
        {
            if (artworkItem.CoverUrl is not null)
                artworkItem.CoverUrl = AbsoluteUrl(artworkItem.CoverUrl);
        }
    }
}
