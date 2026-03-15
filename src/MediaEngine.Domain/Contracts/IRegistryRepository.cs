using System.Text.Json.Serialization;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Provides paginated, filtered access to the unified registry view — joining
/// works, canonical values, review queue, metadata claims, and media assets
/// into a single scannable listing.
/// </summary>
public interface IRegistryRepository
{
    /// <summary>
    /// Returns a paginated list of registry items with optional filtering by
    /// search text, media type, status, minimum confidence, and match source.
    /// </summary>
    Task<RegistryPageResult> GetPageAsync(RegistryQuery query, CancellationToken ct = default);

    /// <summary>
    /// Returns the full detail for a single registry item, including all
    /// canonical values, claim history, and review queue data.
    /// </summary>
    Task<RegistryItemDetail?> GetDetailAsync(Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Returns counts for each status category (All, Review, Auto, Edited, Duplicate).
    /// </summary>
    Task<RegistryStatusCounts> GetStatusCountsAsync(CancellationToken ct = default);
}

/// <summary>Counts for status tab badges.</summary>
public sealed record RegistryStatusCounts(
    [property: JsonPropertyName("total")]        int Total,
    [property: JsonPropertyName("needs_review")] int NeedsReview,
    [property: JsonPropertyName("auto_approved")]int AutoApproved,
    [property: JsonPropertyName("edited")]       int Edited,
    [property: JsonPropertyName("duplicate")]    int Duplicate);
