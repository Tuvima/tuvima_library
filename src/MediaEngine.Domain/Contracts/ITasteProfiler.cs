using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Builds and updates local taste profiles from library patterns.
/// </summary>
public interface ITasteProfiler
{
    /// <summary>Get or build the taste profile for a user.</summary>
    Task<TasteProfileBuildResult> GetProfileAsync(Guid userId, CancellationToken ct = default);

}
