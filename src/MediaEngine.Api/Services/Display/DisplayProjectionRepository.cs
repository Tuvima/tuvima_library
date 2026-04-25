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

    public DisplayProjectionRepository(
        DisplayWorkProjectionReader works,
        DisplayJourneyProjectionReader journey,
        DisplayFavoriteProjectionReader favorites)
    {
        _works = works;
        _journey = journey;
        _favorites = favorites;
    }

    public Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct) =>
        _works.LoadAsync(ct);

    public Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct) =>
        _journey.LoadAsync(lane, ct);

    public Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct) =>
        _favorites.LoadAsync(profileId, ct);
}
