using MediaEngine.Web.Models.ViewDTOs;
using MediaEngine.Web.Services.Integration;
using MudBlazor;

namespace MediaEngine.Web.Services.Playback;

public enum MediaReaction
{
    Neutral,
    Like,
    Dislike,
}

public sealed class MediaReactionService
{
    private const string FavoritesCollectionName = "Favorites";
    private const string DislikedCollectionName = "Disliked Media";
    private const int CollectionItemFetchLimit = 4000;

    private readonly IEngineApiClient _apiClient;
    private readonly Dictionary<Guid, ProfileReactionState> _cache = [];

    public MediaReactionService(IEngineApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public async Task<MediaReaction> GetReactionAsync(Guid workId, Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollections: false, ct);
        if (state is null)
        {
            return MediaReaction.Neutral;
        }

        if (state.DislikedWorkIds.Contains(workId))
        {
            return MediaReaction.Dislike;
        }

        return state.FavoriteWorkIds.Contains(workId)
            ? MediaReaction.Like
            : MediaReaction.Neutral;
    }

    public async Task<IReadOnlyCollection<Guid>> GetFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollections: false, ct);
        return state is null ? [] : state.FavoriteWorkIds.ToList();
    }

    public async Task<IReadOnlyCollection<Guid>> GetDislikedWorkIdsAsync(Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollections: false, ct);
        return state is null ? [] : state.DislikedWorkIds.ToList();
    }

    public async Task SetReactionAsync(Guid workId, MediaReaction reaction, Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollections: reaction is not MediaReaction.Neutral, ct);
        if (state is null)
        {
            return;
        }

        if (reaction is MediaReaction.Neutral or MediaReaction.Dislike)
        {
            await RemoveFromCollectionIfPresentAsync(workId, state.FavoritesCollectionId, state.FavoriteItemsByWorkId, profileId, ct);
        }

        if (reaction is MediaReaction.Neutral or MediaReaction.Like)
        {
            await RemoveFromCollectionIfPresentAsync(workId, state.DislikedCollectionId, state.DislikedItemsByWorkId, profileId, ct);
        }

        if (reaction == MediaReaction.Like && state.FavoritesCollectionId.HasValue && !state.FavoriteWorkIds.Contains(workId))
        {
            if (await _apiClient.AddCollectionItemAsync(state.FavoritesCollectionId.Value, workId, profileId, ct))
            {
                await ReloadStateAsync(profileId!.Value, ct);
            }
        }

        if (reaction == MediaReaction.Dislike && state.DislikedCollectionId.HasValue && !state.DislikedWorkIds.Contains(workId))
        {
            if (await _apiClient.AddCollectionItemAsync(state.DislikedCollectionId.Value, workId, profileId, ct))
            {
                await ReloadStateAsync(profileId!.Value, ct);
            }
        }
    }

    public async Task<ManagedCollectionViewModel?> GetFavoritesCollectionAsync(Guid? profileId, CancellationToken ct = default)
    {
        var state = await GetStateAsync(profileId, createCollections: false, ct);
        return state?.FavoritesCollection;
    }

    public async Task<List<ManagedCollectionViewModel>> GetPlaylistCollectionsAsync(Guid? profileId, CancellationToken ct = default)
    {
        if (!profileId.HasValue)
        {
            return [];
        }

        var collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
        return collections
            .Where(collection =>
                string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
                && collection.ProfileId == profileId)
            .OrderBy(collection => collection.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task RefreshAsync(Guid? profileId, CancellationToken ct = default)
    {
        if (!profileId.HasValue)
        {
            return;
        }

        await ReloadStateAsync(profileId.Value, ct);
    }

    private async Task RemoveFromCollectionIfPresentAsync(
        Guid workId,
        Guid? collectionId,
        Dictionary<Guid, Guid> itemsByWorkId,
        Guid? profileId,
        CancellationToken ct)
    {
        if (!collectionId.HasValue || !itemsByWorkId.TryGetValue(workId, out var itemId))
        {
            return;
        }

        if (await _apiClient.RemoveCollectionItemAsync(collectionId.Value, itemId, profileId, ct) && profileId.HasValue)
        {
            await ReloadStateAsync(profileId.Value, ct);
        }
    }

    private async Task<ProfileReactionState?> GetStateAsync(Guid? profileId, bool createCollections, CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return null;
        }

        if (_cache.TryGetValue(profileId.Value, out var cached))
        {
            if (!createCollections || (cached.FavoritesCollectionId.HasValue && cached.DislikedCollectionId.HasValue))
            {
                return cached;
            }
        }

        return await ReloadStateAsync(profileId.Value, ct, createCollections);
    }

    private async Task<ProfileReactionState> ReloadStateAsync(Guid profileId, CancellationToken ct, bool createCollections = false)
    {
        var collections = await _apiClient.GetManagedCollectionsAsync(profileId, ct);
        var favorites = collections.FirstOrDefault(collection =>
            string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collection.Name, FavoritesCollectionName, StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == profileId);
        var disliked = collections.FirstOrDefault(collection =>
            string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
            && string.Equals(collection.Name, DislikedCollectionName, StringComparison.OrdinalIgnoreCase)
            && collection.ProfileId == profileId);

        if (createCollections && favorites is null)
        {
            await _apiClient.CreateCollectionAsync(
                FavoritesCollectionName,
                "Profile-level likes and favorites across the library.",
                Icons.Material.Outlined.Grade,
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
            favorites = collections.FirstOrDefault(collection =>
                string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
                && string.Equals(collection.Name, FavoritesCollectionName, StringComparison.OrdinalIgnoreCase)
                && collection.ProfileId == profileId);
        }

        if (createCollections && disliked is null)
        {
            await _apiClient.CreateCollectionAsync(
                DislikedCollectionName,
                "Profile-level dislikes across the library.",
                Icons.Material.Outlined.ThumbDownOffAlt,
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
            disliked = collections.FirstOrDefault(collection =>
                string.Equals(collection.CollectionType, "Playlist", StringComparison.OrdinalIgnoreCase)
                && string.Equals(collection.Name, DislikedCollectionName, StringComparison.OrdinalIgnoreCase)
                && collection.ProfileId == profileId);
        }

        var favoriteItems = favorites is not null
            ? await _apiClient.GetCollectionItemsAsync(favorites.Id, CollectionItemFetchLimit, profileId, ct)
            : [];
        var dislikedItems = disliked is not null
            ? await _apiClient.GetCollectionItemsAsync(disliked.Id, CollectionItemFetchLimit, profileId, ct)
            : [];

        var state = new ProfileReactionState
        {
            FavoritesCollection = favorites,
            FavoritesCollectionId = favorites?.Id,
            DislikedCollection = disliked,
            DislikedCollectionId = disliked?.Id,
            FavoriteWorkIds = favoriteItems.Select(item => item.WorkId).ToHashSet(),
            DislikedWorkIds = dislikedItems.Select(item => item.WorkId).ToHashSet(),
            FavoriteItemsByWorkId = favoriteItems
                .GroupBy(item => item.WorkId)
                .ToDictionary(group => group.Key, group => group.First().Id),
            DislikedItemsByWorkId = dislikedItems
                .GroupBy(item => item.WorkId)
                .ToDictionary(group => group.Key, group => group.First().Id),
        };

        _cache[profileId] = state;
        return state;
    }

    private sealed class ProfileReactionState
    {
        public ManagedCollectionViewModel? FavoritesCollection { get; set; }
        public Guid? FavoritesCollectionId { get; set; }
        public ManagedCollectionViewModel? DislikedCollection { get; set; }
        public Guid? DislikedCollectionId { get; set; }
        public HashSet<Guid> FavoriteWorkIds { get; set; } = [];
        public HashSet<Guid> DislikedWorkIds { get; set; } = [];
        public Dictionary<Guid, Guid> FavoriteItemsByWorkId { get; set; } = [];
        public Dictionary<Guid, Guid> DislikedItemsByWorkId { get; set; } = [];
    }
}
