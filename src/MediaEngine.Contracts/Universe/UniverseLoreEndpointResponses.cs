namespace MediaEngine.Contracts.Universe;

/// <summary>
/// Response body for the <c>/universe/{qid}/lore-sources*</c> endpoints. Property
/// names are byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record UniverseLoreSourceResponse(
    Guid id,
    string universe_qid,
    string plugin_id,
    string source_key,
    string source_name,
    string base_url,
    string api_url,
    string status,
    double confidence,
    string? license,
    DateTimeOffset? approved_at,
    string? approved_by,
    DateTimeOffset? rejected_at,
    DateTimeOffset? last_discovered_at,
    DateTimeOffset? last_enriched_at,
    DateTimeOffset created_at,
    DateTimeOffset updated_at);

/// <summary>
/// Response body for <c>POST /universe/{qid}/lore/enrich</c>. Property names are
/// byte-identical to the anonymous type this record replaces — no
/// <c>[JsonPropertyName]</c> needed.
/// </summary>
public sealed record UniverseLoreEnrichResponse(
    string universe_qid,
    int sources_enriched,
    int entities_written,
    int relationships_written);
