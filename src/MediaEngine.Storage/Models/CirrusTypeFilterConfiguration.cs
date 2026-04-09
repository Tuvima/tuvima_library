using System.Text.Json.Serialization;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Per-media-type Wikidata P31 allow-lists applied to CirrusSearch text
/// reconciliation. Loaded from <c>config/cirrus-type-filters.json</c> and
/// consumed by <c>ReconciliationAdapter.BuildStage2Request</c> when falling
/// back to <c>TextStage2Request</c>.
///
/// <para>
/// The library's <c>TextStage2Request</c> requires a non-empty
/// <c>CirrusSearchTypes</c> list (the strict "no unfiltered text" rule),
/// so media types missing from this map cause text fallback to be skipped
/// entirely — those items route directly to review.
/// </para>
/// </summary>
public sealed class CirrusTypeFilterConfiguration
{
    /// <summary>
    /// Informational hint for hand-editing. Not read by code.
    /// </summary>
    [JsonPropertyName("$schema_hint")]
    public string? SchemaHint { get; set; }

    /// <summary>
    /// Media-type → P31 QID list map. Keys are lower-case media type names
    /// (e.g. <c>"books"</c>, <c>"tv"</c>, <c>"podcasts"</c>).
    /// </summary>
    [JsonPropertyName("filters")]
    public Dictionary<string, List<string>> Filters { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the allow-list for the given media type, or an empty list when
    /// the media type is not configured. Callers should interpret an empty
    /// list as "skip text fallback for this media type".
    /// </summary>
    public IReadOnlyList<string> GetTypesFor(MediaType mediaType)
    {
        var key = mediaType.ToString().ToLowerInvariant();
        return Filters.TryGetValue(key, out var types) ? types : [];
    }
}
