namespace MediaEngine.Api.Services.Display;

public interface IDisplayProjectionReadService
{
    Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct);

    Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(
        string? lane,
        CancellationToken ct);

    Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(
        Guid? profileId,
        CancellationToken ct);

    Task<IReadOnlyList<DisplayHomeCollectionRow>> LoadHomeCollectionsAsync(
        Guid? profileId,
        CancellationToken ct);

    Task<IReadOnlySet<Guid>> LoadHiddenWorkIdsAsync(
        Guid? profileId,
        CancellationToken ct);
}
