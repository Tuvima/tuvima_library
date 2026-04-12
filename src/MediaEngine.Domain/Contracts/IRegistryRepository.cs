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

    /// <summary>
    /// Returns a dictionary of media type → count across all works in the library.
    /// </summary>
    Task<Dictionary<string, int>> GetMediaTypeCountsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns aggregate counts derived from the shared registry pipeline projection.
    /// </summary>
    Task<RegistryProjectionSummary> GetProjectionSummaryAsync(CancellationToken ct = default);
}

/// <summary>
/// Four-state counts for the Registry: Identified, InReview, Provisional, Rejected.
/// Includes per-trigger breakdown within InReview (e.g. "LowConfidence" → 25).
/// </summary>
public sealed record RegistryFourStateCounts(
    [property: JsonPropertyName("identified")]     int Identified,
    [property: JsonPropertyName("in_review")]      int InReview,
    [property: JsonPropertyName("provisional")]    int Provisional,
    [property: JsonPropertyName("rejected")]       int Rejected,
    [property: JsonPropertyName("person_count")]   int PersonCount,
    [property: JsonPropertyName("collection_count")]      int CollectionCount,
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

/// <summary>Aggregate counts derived from the shared registry pipeline projection.</summary>
public sealed record RegistryProjectionSummary(
    [property: JsonPropertyName("total_items")] int TotalItems,
    [property: JsonPropertyName("with_qid")] int WithQid,
    [property: JsonPropertyName("without_qid")] int WithoutQid,
    [property: JsonPropertyName("enriched_stage3")] int EnrichedStage3,
    [property: JsonPropertyName("not_enriched_stage3")] int NotEnrichedStage3,
    [property: JsonPropertyName("universe_assigned")] int UniverseAssigned,
    [property: JsonPropertyName("universe_unassigned")] int UniverseUnassigned,
    [property: JsonPropertyName("stale_items")] int StaleItems,
    [property: JsonPropertyName("hidden_by_quality_gate")] int HiddenByQualityGate,
    [property: JsonPropertyName("art_pending")] int ArtPending,
    [property: JsonPropertyName("retail_needs_review")] int RetailNeedsReview,
    [property: JsonPropertyName("qid_no_match")] int QidNoMatch,
    [property: JsonPropertyName("completed_with_art")] int CompletedWithArt);
