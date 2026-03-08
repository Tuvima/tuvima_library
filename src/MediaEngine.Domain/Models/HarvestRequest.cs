using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

/// <summary>
/// A unit of work placed on the metadata harvest queue.
///
/// Carries enough context for the <c>MetadataHarvestingService</c> to route the
/// request to the correct external provider adapters and build well-formed
/// <see cref="Entities.MetadataClaim"/> rows from the returned claims.
///
/// Spec: Phase 9 – Non-Blocking Harvesting § Queue Item.
/// </summary>
public sealed class HarvestRequest
{
    /// <summary>
    /// The domain entity this request enriches.
    /// Points to a <c>media_assets.id</c>, <c>persons.id</c>, etc., depending on
    /// <see cref="EntityType"/>.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>The kind of entity being harvested.</summary>
    public required EntityType EntityType { get; init; }

    /// <summary>
    /// The media type of the asset being harvested.
    /// Used by adapters to decide which endpoint / entity type to query.
    /// <see cref="MediaType.Unknown"/> is valid for Person enrichment requests
    /// where a media type is not applicable.
    /// </summary>
    public required MediaType MediaType { get; init; }

    /// <summary>
    /// Contextual hints for the adapter.
    /// Common keys: <c>"title"</c>, <c>"author"</c>, <c>"asin"</c>,
    /// <c>"isbn"</c>, <c>"narrator"</c>, <c>"name"</c>, <c>"role"</c>.
    ///
    /// Adapters must never fail if a hint key is absent; they return an empty
    /// result list instead.
    /// </summary>
    public IReadOnlyDictionary<string, string> Hints { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// An already-resolved Wikidata QID to use for Stage 2+3 hydration.
    ///
    /// When set, the pipeline skips QID resolution in Stage 2 and goes straight
    /// to SPARQL deep hydration using this QID. Used when the user resolves a
    /// disambiguation review item by selecting a specific QID candidate.
    /// </summary>
    public string? PreResolvedQid { get; init; }

    /// <summary>
    /// When <c>true</c>, this request is a re-enqueue for cover art download
    /// after auto-organization. The pipeline still runs (claims are appended,
    /// cover is downloaded) but the <c>MediaAdded</c> activity entry is suppressed
    /// to avoid duplicating the original entry.
    /// </summary>
    public bool SuppressActivityEntry { get; init; }
}
