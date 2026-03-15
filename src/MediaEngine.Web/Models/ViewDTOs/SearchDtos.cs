using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

// ── Universe Search ───────────────────────────────────────────────────────────

/// <summary>Request sent to POST /search/universe.</summary>
public sealed class SearchUniverseRequestDto
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 5;
}

/// <summary>A single enriched Wikidata candidate returned from universe search.</summary>
public sealed class UniverseCandidateDto
{
    [JsonPropertyName("qid")]
    public string Qid { get; set; } = "";

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("instance_of")]
    public string? InstanceOf { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("wikipedia_extract")]
    public string? WikipediaExtract { get; set; }

    [JsonPropertyName("resolution_tier")]
    public string? ResolutionTier { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; set; } = [];
}

/// <summary>Response from POST /search/universe.</summary>
public sealed class SearchUniverseResponseDto
{
    [JsonPropertyName("candidates")]
    public List<UniverseCandidateDto> Candidates { get; set; } = [];

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";
}

// ── Retail Search ─────────────────────────────────────────────────────────────

/// <summary>Request sent to POST /search/retail.</summary>
public sealed class SearchRetailRequestDto
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; set; } = 5;
}

/// <summary>A single retail provider candidate.</summary>
public sealed class RetailCandidateDto
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; set; } = "";

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "";

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

/// <summary>Response from POST /search/retail.</summary>
public sealed class SearchRetailResponseDto
{
    [JsonPropertyName("candidates")]
    public List<RetailCandidateDto> Candidates { get; set; } = [];

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = "";
}

// ── Apply Match ───────────────────────────────────────────────────────────────

/// <summary>Request sent to POST /registry/items/{entityId}/apply-match.</summary>
public sealed class ApplyMatchRequestDto
{
    /// <summary>"Universe" or "Retail".</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "Universe";

    [JsonPropertyName("qid")]
    public string? Qid { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("director")]
    public string? Director { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }
}

/// <summary>Response from POST /registry/items/{entityId}/apply-match.</summary>
public sealed class ApplyMatchResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string WikidataStatus { get; set; } = "";

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; set; }

    [JsonPropertyName("hydration_triggered")]
    public bool HydrationTriggered { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// ── Create Manual Entry ───────────────────────────────────────────────────────

/// <summary>Request sent to POST /registry/items/{entityId}/create-manual.</summary>
public sealed class CreateManualRequestDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>Response from POST /registry/items/{entityId}/create-manual.</summary>
public sealed class CreateManualResponseDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("wikidata_status")]
    public string WikidataStatus { get; set; } = "manual";

    [JsonPropertyName("claims_written")]
    public int ClaimsWritten { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
