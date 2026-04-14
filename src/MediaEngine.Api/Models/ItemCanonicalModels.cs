using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class ItemPreferencesRequest
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemPreferencesResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("fields_updated")]
    public int FieldsUpdated { get; init; }

    [JsonPropertyName("updated_keys")]
    public IReadOnlyList<string> UpdatedKeys { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public sealed class ItemCanonicalSearchRequest
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("target_kind")]
    public string TargetKind { get; init; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; init; } = "";

    [JsonPropertyName("draft_fields")]
    public Dictionary<string, string> DraftFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("query_override")]
    public string? QueryOverride { get; init; }

    [JsonPropertyName("max_candidates")]
    public int MaxCandidates { get; init; } = 6;
}

public sealed class ItemCanonicalSearchResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "";

    [JsonPropertyName("target_kind")]
    public string TargetKind { get; init; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; init; } = "";

    [JsonPropertyName("query")]
    public string Query { get; init; } = "";

    [JsonPropertyName("retail_candidates")]
    public IReadOnlyList<ItemCanonicalRetailCandidate> RetailCandidates { get; init; } = [];

    [JsonPropertyName("linked_candidates")]
    public IReadOnlyList<ItemCanonicalLinkedCandidate> LinkedCandidates { get; init; } = [];

    [JsonPropertyName("fallback_actions")]
    public IReadOnlyList<string> FallbackActions { get; init; } = [];

    [JsonPropertyName("no_result_message")]
    public string? NoResultMessage { get; init; }

    [JsonPropertyName("can_apply_unlinked_canonical")]
    public bool CanApplyUnlinkedCanonical { get; init; }

    [JsonPropertyName("missing_required_fields")]
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = [];

    [JsonPropertyName("unlinked_fields")]
    public Dictionary<string, string> UnlinkedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("draft_fields")]
    public Dictionary<string, string> DraftFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemCanonicalRetailCandidate
{
    [JsonPropertyName("candidate_id")]
    public string CandidateId { get; init; } = "";

    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = "";

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = "";

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("extra_fields")]
    public Dictionary<string, string> ExtraFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("composite_score")]
    public double CompositeScore { get; init; }

    [JsonPropertyName("link_state")]
    public string LinkState { get; init; } = "provider_only";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; init; } = "Linked to provider only";

    [JsonPropertyName("is_applicable")]
    public bool IsApplicable { get; init; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; init; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemCanonicalLinkedCandidate
{
    [JsonPropertyName("candidate_id")]
    public string CandidateId { get; init; } = "";

    [JsonPropertyName("qid")]
    public string Qid { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("instance_of")]
    public string? InstanceOf { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("wikipedia_extract")]
    public string? WikipediaExtract { get; init; }

    [JsonPropertyName("resolution_tier")]
    public string? ResolutionTier { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("media_type_metadata")]
    public Dictionary<string, string> MediaTypeMetadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("link_state")]
    public string LinkState { get; init; } = "linked";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; init; } = "Linked to Wikidata";

    [JsonPropertyName("is_applicable")]
    public bool IsApplicable { get; init; }

    [JsonPropertyName("blocked_reason")]
    public string? BlockedReason { get; init; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemCanonicalApplyRequest
{
    [JsonPropertyName("target_kind")]
    public string TargetKind { get; init; } = "";

    [JsonPropertyName("target_field_group")]
    public string TargetFieldGroup { get; init; } = "";

    [JsonPropertyName("link_state")]
    public string LinkState { get; init; } = "";

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; init; }

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }

    [JsonPropertyName("required_fields")]
    public Dictionary<string, string> RequiredFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("suggested_fields")]
    public Dictionary<string, string> SuggestedFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("accepted_suggested_keys")]
    public List<string> AcceptedSuggestedKeys { get; init; } = [];

    [JsonPropertyName("bridge_ids")]
    public Dictionary<string, string> BridgeIds { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("qid_fields")]
    public Dictionary<string, string> QidFields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ItemCanonicalApplyResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("link_state")]
    public string LinkState { get; init; } = "";

    [JsonPropertyName("link_status_label")]
    public string LinkStatusLabel { get; init; } = "";

    [JsonPropertyName("fields_applied")]
    public int FieldsApplied { get; init; }

    [JsonPropertyName("ids_cleared")]
    public IReadOnlyList<string> IdsCleared { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}
