using System.Text.Json.Serialization;

namespace Tanaste.Providers.Models;

/// <summary>
/// One entry in the ordered bridge-lookup priority list.
///
/// When resolving a Wikidata QID from external identifiers, the adapter
/// tries each entry in order; the first match wins.
/// </summary>
public sealed class BridgeLookupEntry
{
    /// <summary>The Wikidata property code used for lookup, e.g. <c>"P3861"</c>.</summary>
    [JsonPropertyName("p_code")]
    public string PCode { get; set; } = string.Empty;

    /// <summary>
    /// The property name on the lookup request that supplies the search value.
    /// Maps to a field on the ingested media asset's metadata
    /// (e.g. <c>"asin"</c>, <c>"isbn"</c>, <c>"apple_books_id"</c>).
    /// </summary>
    [JsonPropertyName("request_field")]
    public string RequestField { get; set; } = string.Empty;
}
