using MediaEngine.Domain.Models;
namespace MediaEngine.Domain.Contracts;

public interface ITvEpisodeCreditRepository
{
    Task ReplaceAsync(TvEpisodeCredits credits, CancellationToken ct = default);
    Task<IReadOnlyList<TvEpisodeCredits>> ReadAsync(Guid workOrShowId, CancellationToken ct = default);
}
