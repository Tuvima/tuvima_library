using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Services.Playback;

public sealed record FavoriteMembership(Guid CollectionId, Guid? ItemId, bool IsFavorite);
public sealed record FavoriteListSnapshot(Guid? CollectionId, IReadOnlyList<CollectionItemViewModel> Items);

public sealed class FavoriteService : IDisposable
{
    private const string FavoritesCollectionName = "Favorites";
    private const int CollectionItemFetchLimit = 4000;

    private readonly IEngineApiClient _apiClient;
    private readonly Dictionary<Guid, FavoriteState> _cache = [];
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public FavoriteService(IEngineApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public event Action<Guid>? Changed;

    public async Task<FavoriteListSnapshot> GetListAsync(Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollection: false, ct);
        return state is null
            ? new FavoriteListSnapshot(null, [])
            : new FavoriteListSnapshot(state.CollectionId, state.Items.ToList());
    }

    public async Task<IReadOnlyCollection<Guid>> GetFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollection: false, ct);
        return state is null ? [] : state.WorkIds.ToList();
    }

    public async Task<FavoriteMembership?> GetMembershipAsync(Guid workId, Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollection: false, ct);
        if (state?.CollectionId is not Guid collectionId)
            return null;

        var isFavorite = state.ItemIdsByWorkId.TryGetValue(workId, out var itemId);
        return new FavoriteMembership(collectionId, isFavorite ? itemId : null, isFavorite);
    }

    public async Task<FavoriteMembership?> ToggleAsync(Guid workId, Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollection: false, ct);
        if (state?.CollectionId is null)
        {
            state = await GetStateAsync(profileId, createCollection: true, ct);
        }

        if (state?.CollectionId is not Guid collectionId)
            return null;

        var isFavorite = state.ItemIdsByWorkId.TryGetValue(workId, out var itemId);
        var membership = new FavoriteMembership(collectionId, isFavorite ? itemId : null, isFavorite);

        if (membership.IsFavorite && membership.ItemId.HasValue)
        {
            var removed = await _apiClient.RemoveCollectionItemAsync(membership.CollectionId, membership.ItemId.Value, profileId, ct);
            if (removed && profileId.HasValue)
            {
                await ReloadStateAsync(profileId.Value, ct);
                Changed?.Invoke(profileId.Value);
            }

            return removed
                ? membership with { ItemId = null, IsFavorite = false }
                : membership;
        }

        var added = await _apiClient.AddCollectionItemAsync(membership.CollectionId, workId, profileId, ct);
        if (added && profileId.HasValue)
        {
            await ReloadStateAsync(profileId.Value, ct);
            Changed?.Invoke(profileId.Value);
        }

        return added
            ? await GetMembershipAsync(workId, profileId, ct)
            : membership;
    }

    public async Task RefreshAsync(Guid? profileId, CancellationToken ct = default)
    {
        if (profileId.HasValue)
            await ReloadStateAsync(profileId.Value, ct);
    }

    private async Task<FavoriteState?> GetStateAsync(Guid? profileId, bool createCollection, CancellationToken ct)
    {
        if (!profileId.HasValue)
            return null;

        if (_cache.TryGetValue(profileId.Value, out var cached) && (!createCollection || cached.CollectionId.HasValue))
            return cached;

        await _stateGate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(profileId.Value, out cached) && (!createCollection || cached.CollectionId.HasValue))
                return cached;

            return await ReloadStateAsync(profileId.Value, ct, createCollection);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<FavoriteState> ReloadStateAsync(Guid profileId, CancellationToken ct, bool createCollection = false)
    {
        var collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
        var favorites = FindFavoritesCollection(collections, profileId);

        if (createCollection && favorites is null)
        {
            await _apiClient.CreateCollectionAsync(
                FavoritesCollectionName,
                "Profile-level favorites across the library.",
                Icons.Material.Outlined.FavoriteBorder,
                "Playlist",
                [],
                "all",
                null,
                "desc",
                false,
                "private",
                profileId,
                ct);

            collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
            favorites = FindFavoritesCollection(collections, profileId);
        }

        var items = favorites is not null
            ? await _apiClient.GetCollectionItemsAsync(favorites.Id, CollectionItemFetchLimit, profileId, ct)
            : [];

        var state = new FavoriteState
        {
            CollectionId = favorites?.Id,
            Items = items.ToList(),
            WorkIds = items.Select(item => item.WorkId).ToHashSet(),
            ItemIdsByWorkId = items
                .GroupBy(item => item.WorkId)
                .ToDictionary(group => group.Key, group => group.First().Id),
        };

        _cache[profileId] = state;
        return state;
    }

    private static ManagedCollectionViewModel? FindFavoritesCollection(IEnumerable<ManagedCollectionViewModel> collections, Guid profileId)
        => collections.FirstOrDefault(collection =>
            string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collection.Name, FavoritesCollectionName, StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == profileId);

    private sealed class FavoriteState
    {
        public Guid? CollectionId { get; init; }
        public IReadOnlyList<CollectionItemViewModel> Items { get; init; } = [];
        public HashSet<Guid> WorkIds { get; init; } = [];
        public Dictionary<Guid, Guid> ItemIdsByWorkId { get; init; } = [];
    }

    public void Dispose() => _stateGate.Dispose();
}
