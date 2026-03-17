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
