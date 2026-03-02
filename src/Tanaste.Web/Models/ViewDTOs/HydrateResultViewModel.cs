using System.Text.Json.Serialization;

namespace Tanaste.Web.Models.ViewDTOs;

/// <summary>
/// Result of a manual Wikidata hydration triggered from the Dashboard.
/// Returned by <c>POST /metadata/hydrate/{entityId}</c>.
/// </summary>
public sealed class HydrateResultViewModel
{
    /// <summary>The Wikidata Q-identifier resolved for this entity, if any.</summary>
    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    /// <summary>Number of new metadata claims written to the database.</summary>
    [JsonPropertyName("claims_added")]
    public int ClaimsAdded { get; init; }

    /// <summary>Whether the hydration completed successfully.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>Human-readable result message.</summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}
