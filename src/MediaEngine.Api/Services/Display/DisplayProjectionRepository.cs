using Microsoft.Extensions.Caching.Memory;

namespace MediaEngine.Api.Services.Display;

public interface IDisplayProjectionRepository
{
    Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct);
    Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct);
    Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct);
}

public sealed class DisplayProjectionRepository : IDisplayProjectionRepository
{
    private readonly DisplayWorkProjectionReader _works;
    private readonly DisplayJourneyProjectionReader _journey;
    private readonly DisplayFavoriteProjectionReader _favorites;
    private readonly IMemoryCache _cache;

    public DisplayProjectionRepository(
        DisplayWorkProjectionReader works,
        DisplayJourneyProjectionReader journey,
        DisplayFavoriteProjectionReader favorites,
        IMemoryCache cache)
    {
        _works = works;
        _journey = journey;
        _favorites = favorites;
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
}


