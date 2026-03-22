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

    /// <summary>
    /// Returns counts for the four lifecycle states (Registered, InReview, Provisional, Rejected)
    /// plus a per-trigger breakdown within InReview. Optionally scoped to a single batch.
    /// </summary>
    Task<RegistryFourStateCounts> GetFourStateCountsAsync(Guid? batchId = null, CancellationToken ct = default);
}

/// <summary>
/// Four-state counts for the Registry: Registered, InReview, Provisional, Rejected.
/// Includes per-trigger breakdown within InReview (e.g. "LowConfidence" → 25).
/// </summary>
public sealed record RegistryFourStateCounts(
    [property: JsonPropertyName("registered")]     int Registered,
    [property: JsonPropertyName("in_review")]      int InReview,
    [property: JsonPropertyName("provisional")]    int Provisional,
    [property: JsonPropertyName("rejected")]       int Rejected,
    [property: JsonPropertyName("person_count")]   int PersonCount,
    [property: JsonPropertyName("hub_count")]      int HubCount,
    [property: JsonPropertyName("trigger_counts")] IReadOnlyDictionary<string, int> TriggerCounts);

/// <summary>Counts for status tab badges.</summary>
public sealed record RegistryStatusCounts(
    [property: JsonPropertyName("total")]            int Total,
    [property: JsonPropertyName("needs_review")]     int NeedsReview,
    [property: JsonPropertyName("auto_approved")]    int AutoApproved,
    [property: JsonPropertyName("edited")]           int Edited,
    [property: JsonPropertyName("duplicate")]        int Duplicate,
    [property: JsonPropertyName("staging")]          int Staging = 0,
    [property: JsonPropertyName("missing_images")]   int MissingImages = 0,
    [property: JsonPropertyName("recently_updated")] int RecentlyUpdated = 0,
    [property: JsonPropertyName("low_confidence")]   int LowConfidence = 0,
    [property: JsonPropertyName("rejected")]         int Rejected = 0);
