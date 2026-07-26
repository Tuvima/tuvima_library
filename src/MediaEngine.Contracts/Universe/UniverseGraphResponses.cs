namespace MediaEngine.Contracts.Universe;

/// <summary>
/// Wire responses for <c>UniverseGraphEndpoints</c> routes that previously returned anonymous
/// types (<c>Results.Ok(new { ... })</c>). Property names are deliberately left exactly as the
/// anonymous types declared them — camelCase or snake_case, not PascalCase — and carry no
/// <c>[JsonPropertyName]</c> overrides, so the JSON payload these records produce is
/// byte-identical to what the replaced anonymous types produced.
///
/// <para>
/// Named <c>UniverseListItemDto</c> rather than <c>UniverseSummaryDto</c> to avoid a
/// same-name collision with <see cref="MediaEngine.Domain.Models.UniverseSummaryDto"/>, which
/// <c>UniverseGraphEndpoints.cs</c> already imports unqualified via
/// <c>using MediaEngine.Domain.Models;</c> — reusing that name here would make every
/// unqualified reference in that file ambiguous (CS0104).
/// </para>
/// </summary>
public sealed record UniverseListItemDto(
    string qid,
    string label,
    string level,
    string? parent_qid,
    int entity_count,
    int character_count,
    int location_count,
    int organization_count,
    int event_count,
    int relationship_count,
    bool has_graph,
    string enrichment_status);

public sealed record UniverseDetailResponse(
    UniverseDetailRef universe,
    int entity_count,
    int character_count,
    int location_count,
    int organization_count,
    int relationship_count);

public sealed record UniverseDetailRef(string qid, string label, string level);

public sealed record UniverseHealthResponse(
    string qid,
    string label,
    int entities_total,
    int entities_enriched,
    int entities_with_images,
    int relationships_total,
    double health_percent);

public sealed record UniverseGraphResponse(
    UniverseGraphRef universe,
    IReadOnlyList<UniverseGraphNodeDto> nodes,
    IReadOnlyList<UniverseGraphEdgeDto> edges);

public sealed record UniverseGraphRef(string qid, string label);

public sealed record UniverseGraphNodeDto(
    string id,
    string label,
    string type,
    string? description,
    string? image,
    IEnumerable<UniverseGraphWorkLinkDto> works,
    bool supplemental,
    string provenance,
    string? source_plugin,
    string? source_url);

public sealed record UniverseGraphWorkLinkDto(string qid, string? label);

public sealed record UniverseGraphEdgeDto(
    string source,
    string target,
    string type,
    string label,
    double confidence,
    string? context_work,
    string? start_time,
    string? end_time,
    bool supplemental,
    string provenance,
    string? source_plugin,
    string? source_url);

public sealed record UniverseDeepEnrichResponse(
    string entity_qid,
    int neighbors_found,
    int enrichment_enqueued,
    string message);

public sealed record UniversePathsResponse(
    string universe_qid,
    string from_qid,
    string to_qid,
    IReadOnlyList<IReadOnlyList<string>> paths);

public sealed record UniverseFamilyTreeResponse(
    string universe_qid,
    string character_qid,
    Dictionary<string, IReadOnlyList<string>> generations);

public sealed record UniverseCrossMediaResponse(
    string universe_qid,
    IReadOnlyList<string> cross_media_entities);

public sealed record UniverseCastResponse(
    string universe_qid,
    IReadOnlyList<UniverseCastCharacterDto> characters);

public sealed record UniverseCastCharacterDto(
    string qid,
    string label,
    string? image,
    string? description,
    IEnumerable<UniverseCastPerformerDto> performers);

public sealed record UniverseCastPerformerDto(
    Guid person_id,
    string? name,
    string? headshot_url,
    string? work_qid,
    int? year);

public sealed record UniverseAdaptationTreeResponse(
    string universe_qid,
    IReadOnlyList<UniverseAdaptationNodeDto> works);

public sealed record UniverseAdaptationNodeDto(
    string qid,
    string label,
    int? year,
    string media_type,
    string? cover_image,
    string? relationship_to_parent,
    IReadOnlyList<UniverseAdaptationNodeDto> children);
