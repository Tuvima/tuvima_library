using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered franchise order recommendations (publication, chronological, recommended).
/// </summary>
public interface IWatchingOrderAdvisor
{
    /// <summary>
    /// Generate a recommended watching/reading order for a Hub or Parent Hub.
    /// Uses text_fast model for on-demand responsiveness.
    /// </summary>
    Task<WatchingOrder> RecommendOrderAsync(
        Guid hubId,
        string orderType,
        CancellationToken ct = default);
}
