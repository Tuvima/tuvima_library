using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>Response from the universe graph API.</summary>
public sealed class UniverseGraphResponse
{
    [JsonPropertyName("universe")]
    public UniverseInfo Universe { get; set; } = new();

    [JsonPropertyName("nodes")]
    public List<GraphNodeDto> Nodes { get; set; } = [];

    [JsonPropertyName("edges")]
    public List<GraphEdgeDto> Edges { get; set; } = [];
}

public sealed class UniverseInfo
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

public sealed class GraphNodeDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("works")]
    public List<GraphNodeWorkLink>? Works { get; set; }
}

public sealed class GraphNodeWorkLink
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;
}

public sealed class GraphEdgeDto
{
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("context_work")]
    public string? ContextWork { get; set; }

    [JsonPropertyName("start_time")]
    public string? StartTime { get; set; }

    [JsonPropertyName("end_time")]
    public string? EndTime { get; set; }
}

public sealed class LoreDeltaResultDto
{
    [JsonPropertyName("entity_qid")]
    public string EntityQid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("cached_revision")]
    public long CachedRevision { get; set; }

    [JsonPropertyName("current_revision")]
    public long CurrentRevision { get; set; }

    [JsonPropertyName("has_changed")]
    public bool HasChanged { get; set; }
}

public sealed class NarrativeRootDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("parent_qid")]
    public string? ParentQid { get; set; }

    [JsonPropertyName("entity_count")]
    public int EntityCount { get; set; }

    [JsonPropertyName("character_count")]
    public int CharacterCount { get; set; }

    [JsonPropertyName("location_count")]
    public int LocationCount { get; set; }

    [JsonPropertyName("organization_count")]
    public int OrganizationCount { get; set; }

    [JsonPropertyName("event_count")]
    public int EventCount { get; set; }

    [JsonPropertyName("relationship_count")]
    public int RelationshipCount { get; set; }

    [JsonPropertyName("has_graph")]
    public bool HasGraph { get; set; }

    [JsonPropertyName("enrichment_status")]
    public string? EnrichmentStatus { get; set; }
}

/// <summary>Response from the on-demand deep enrichment endpoint.</summary>
public sealed class DeepEnrichResponse
{
    [JsonPropertyName("entity_qid")]
    public string EntityQid { get; set; } = string.Empty;

    [JsonPropertyName("neighbors_found")]
    public int NeighborsFound { get; set; }

    [JsonPropertyName("enrichment_enqueued")]
    public int EnrichmentEnqueued { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

// ── Phase 2: Universe Explorer modes ────────────────────────────────────────

/// <summary>Response from GET /universe/{qid}/paths</summary>
public sealed class UniversePathsResponse
{
    [JsonPropertyName("universe_qid")]
    public string UniverseQid { get; set; } = string.Empty;

    [JsonPropertyName("from_qid")]
    public string FromQid { get; set; } = string.Empty;

    [JsonPropertyName("to_qid")]
    public string ToQid { get; set; } = string.Empty;

    [JsonPropertyName("paths")]
    public List<List<PathHopDto>> Paths { get; set; } = [];
}

public sealed class PathHopDto
{
    [JsonPropertyName("entity_qid")]
    public string EntityQid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }
}

/// <summary>Response from GET /universe/{qid}/family-tree</summary>
public sealed class FamilyTreeResponse
{
    [JsonPropertyName("universe_qid")]
    public string UniverseQid { get; set; } = string.Empty;

    [JsonPropertyName("character_qid")]
    public string CharacterQid { get; set; } = string.Empty;

    [JsonPropertyName("generations")]
    public Dictionary<string, List<FamilyMemberDto>> Generations { get; set; } = [];
}

public sealed class FamilyMemberDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }
}

/// <summary>Response from GET /universe/{qid}/cast</summary>
public sealed class UniverseCastResponse
{
    [JsonPropertyName("universe_qid")]
    public string UniverseQid { get; set; } = string.Empty;

    [JsonPropertyName("characters")]
    public List<CastCharacterDto> Characters { get; set; } = [];
}

public sealed class CastCharacterDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("performers")]
    public List<CastPerformerDto> Performers { get; set; } = [];
}

public sealed class CastPerformerDto
{
    [JsonPropertyName("person_id")]
    public string PersonId { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; set; }

    [JsonPropertyName("work_title")]
    public string? WorkTitle { get; set; }

    [JsonPropertyName("year")]
    public int? Year { get; set; }
}

/// <summary>Response from GET /universe/{qid}/adaptations</summary>
public sealed class UniverseAdaptationsResponse
{
    [JsonPropertyName("universe_qid")]
    public string UniverseQid { get; set; } = string.Empty;

    [JsonPropertyName("works")]
    public List<AdaptationNodeDto> Works { get; set; } = [];
}

public sealed class AdaptationNodeDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = string.Empty;

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public int? Year { get; set; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("cover_image")]
    public string? CoverImage { get; set; }

    [JsonPropertyName("relationship_to_parent")]
    public string? RelationshipToParent { get; set; }

    [JsonPropertyName("children")]
    public List<AdaptationNodeDto> Children { get; set; } = [];
}
