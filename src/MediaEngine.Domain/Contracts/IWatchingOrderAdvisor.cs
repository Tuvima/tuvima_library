using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered franchise order recommendations (publication, chronological, recommended).
/// </summary>
public interface IWatchingOrderAdvisor
{
    /// <summary>
    /// Generate a recommended watching/reading order for a Collection or Parent Collection.
    /// Uses text_fast model for on-demand responsiveness.
    /// </summary>
    Task<WatchingOrder> RecommendOrderAsync(
        Guid collectionId,
        string orderType,
        CancellationToken ct = default);
}
