using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A single review queue item as displayed in the Needs Review tab.
/// Maps from <c>GET /review/pending</c> and <c>GET /review/{id}</c>.
/// </summary>
public sealed class ReviewItemViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; set; }

    [JsonPropertyName("entity_type")]
    public string EntityType { get; set; } = string.Empty;

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Pending";

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; set; }

    [JsonPropertyName("proposed_hub_id")]
    public Guid? ProposedHubId { get; set; }

    [JsonPropertyName("candidates_json")]
    public string? CandidatesJson { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; set; }

    [JsonPropertyName("resolved_by")]
    public string? ResolvedBy { get; set; }

    /// <summary>Entity title (best-available), populated from canonical values.</summary>
    [JsonPropertyName("entity_title")]
    public string? EntityTitle { get; set; }

    /// <summary>The media type of the entity (e.g. "Epub", "Audiobook").</summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; set; }

    /// <summary>Cover art URL from canonical "cover" value, if available.</summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }
}

/// <summary>Request body for resolving a review queue item.</summary>
public sealed class ReviewResolveRequestDto
{
    [JsonPropertyName("selected_qid")]
    public string? SelectedQid { get; set; }

    [JsonPropertyName("field_overrides")]
    public List<FieldOverrideDto>? FieldOverrides { get; set; }

    /// <summary>
    /// When resolving via search results, the provider that produced the
    /// selected match (e.g. "apple_books").
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; set; }

    /// <summary>
    /// The provider-specific item identifier for the selected match.
    /// </summary>
    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; set; }
}

/// <summary>A single field override applied when resolving a review item.</summary>
public sealed class FieldOverrideDto
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }
}

/// <summary>Pending review count returned by <c>GET /review/count</c>.</summary>
public sealed record ReviewCountDto(
    [property: JsonPropertyName("pending_count")] int PendingCount);

/// <summary>Hydration pipeline settings DTO for <c>GET/PUT /settings/hydration</c>.</summary>
public sealed class HydrationSettingsDto
{
    [JsonPropertyName("stage_concurrency")]
    public int StageConcurrency { get; set; } = 3;

    [JsonPropertyName("stage1_timeout_seconds")]
    public int Stage1TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("stage2_timeout_seconds")]
    public int Stage2TimeoutSeconds { get; set; } = 45;

    [JsonPropertyName("stage3_timeout_seconds")]
    public int Stage3TimeoutSeconds { get; set; } = 30;

    [JsonPropertyName("disambiguation_threshold")]
    public double DisambiguationThreshold { get; set; } = 0.7;

    [JsonPropertyName("auto_review_confidence_threshold")]
    public double AutoReviewConfidenceThreshold { get; set; } = 0.60;

    [JsonPropertyName("max_qid_candidates")]
    public int MaxQidCandidates { get; set; } = 5;

    [JsonPropertyName("skip_stage2_without_bridge_ids")]
    public bool SkipStage2WithoutBridgeIds { get; set; }
}
