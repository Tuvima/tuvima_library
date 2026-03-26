using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Builds and updates local taste profiles from library patterns.
/// </summary>
public interface ITasteProfiler
{
    /// <summary>Get or build the taste profile for a user.</summary>
    Task<TasteProfile> GetProfileAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Incrementally update the profile after a new item is consumed/rated.</summary>
    Task UpdateAsync(Guid userId, Guid assetId, CancellationToken ct = default);
}
