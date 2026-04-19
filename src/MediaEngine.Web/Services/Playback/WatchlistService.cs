using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Services.Playback;

public sealed record WatchlistMembership(Guid CollectionId, Guid? ItemId, bool IsInWatchlist);

public sealed class WatchlistService
{
    private const string WatchlistName = "Watchlist";

    private readonly IEngineApiClient _apiClient;

    public WatchlistService(IEngineApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<WatchlistMembership?> GetMembershipAsync(Guid workId, Guid? profileId, CancellationToken ct = default)
    {
        var watchlist = await GetOrCreateWatchlistAsync(profileId, ct);
        if (watchlist is null)
            return null;

        var items = await _apiClient.GetCollectionItemsAsync(watchlist.Id, 500, profileId, ct);
        var item = items.FirstOrDefault(entry => entry.WorkId == workId);
        return new WatchlistMembership(watchlist.Id, item?.Id, item is not null);
    }

    public async Task<WatchlistMembership?> ToggleAsync(Guid workId, Guid? profileId, CancellationToken ct = default)
    {
        var membership = await GetMembershipAsync(workId, profileId, ct);
        if (membership is null)
            return null;

        if (membership.IsInWatchlist && membership.ItemId.HasValue)
        {
            var removed = await _apiClient.RemoveCollectionItemAsync(
                membership.CollectionId,
                membership.ItemId.Value,
                profileId,
                ct);

            return removed
                ? membership with { ItemId = null, IsInWatchlist = false }
                : membership;
        }

        var added = await _apiClient.AddCollectionItemAsync(membership.CollectionId, workId, profileId, ct);
        if (!added)
            return membership;

        return await GetMembershipAsync(workId, profileId, ct);
    }

    private async Task<ManagedCollectionViewModel?> GetOrCreateWatchlistAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
            return null;

        var collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
        var watchlist = collections.FirstOrDefault(collection =>
            string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collection.Name, WatchlistName, StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == profileId);

        if (watchlist is not null)
            return watchlist;

        var created = await _apiClient.CreateCollectionAsync(
            WatchlistName,
            "Quick-save shows and movies to watch later.",
            Icons.Material.Outlined.BookmarkAdded,
            "Playlist",
            [],
            "all",
            null,
            "desc",
            false,
            "private",
            profileId,
            ct);

        if (!created)
            return null;

        collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
        return collections.FirstOrDefault(collection =>
            string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collection.Name, WatchlistName, StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == profileId);
    }
}
