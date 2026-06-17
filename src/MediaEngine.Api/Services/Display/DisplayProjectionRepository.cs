using Microsoft.Extensions.Caching.Memory;

namespace MediaEngine.Api.Services.Display;

public interface IDisplayProjectionRepository
{
    Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct);
    Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct);
    Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct);
    Task<IReadOnlyList<DisplayHomeCollectionRow>> LoadHomeCollectionsAsync(Guid? profileId, CancellationToken ct);
}

public sealed class DisplayProjectionRepository : IDisplayProjectionRepository
{
    private readonly DisplayWorkProjectionReader _works;
    private readonly DisplayJourneyProjectionReader _journey;
    private readonly DisplayFavoriteProjectionReader _favorites;
    private readonly DisplayHomeCollectionProjectionReader _homeCollections;
    private readonly IMemoryCache _cache;

    public DisplayProjectionRepository(
        DisplayWorkProjectionReader works,
        DisplayJourneyProjectionReader journey,
        DisplayFavoriteProjectionReader favorites,
        DisplayHomeCollectionProjectionReader homeCollections,
        IMemoryCache cache)
    {
        _works = works;
        _journey = journey;
        _favorites = favorites;
        _homeCollections = homeCollections;
        _cache = cache;
    }

    public async Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct)
    {
        const string cacheKey = "display:works:all";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<DisplayWorkRow>? cached) && cached is not null)
            return cached;

        var rows = await _works.LoadAsync(ct);
        _cache.Set(cacheKey, rows, TimeSpan.FromSeconds(10));
        return rows;
    }

    public Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct) =>
        _journey.LoadAsync(lane, ct);

    public Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct) =>
        _favorites.LoadAsync(profileId, ct);

    public async Task<IReadOnlyList<DisplayHomeCollectionRow>> LoadHomeCollectionsAsync(Guid? profileId, CancellationToken ct)
    {
        var cacheKey = $"display:home-collections:{profileId?.ToString("N") ?? "shared"}";
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<DisplayHomeCollectionRow>? cached) && cached is not null)
            return cached;

        var rows = await _homeCollections.LoadAsync(profileId, ct);
        _cache.Set(cacheKey, rows, TimeSpan.FromSeconds(10));
        return rows;
    }
}


