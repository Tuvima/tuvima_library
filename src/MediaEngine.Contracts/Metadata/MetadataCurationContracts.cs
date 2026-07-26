using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Metadata;

public sealed class ClaimDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("claim_key")] public string ClaimKey { get; init; } = string.Empty;
    [JsonPropertyName("claim_value")] public string ClaimValue { get; init; } = string.Empty;
    [JsonPropertyName("provider_id")] public Guid ProviderId { get; init; }
    [JsonPropertyName("decision_source_provider_id")] public Guid? DecisionSourceProviderId { get; init; }
    [JsonPropertyName("confidence")] public double Confidence { get; init; }
    [JsonPropertyName("is_user_locked")] public bool IsUserLocked { get; init; }
    [JsonPropertyName("claimed_at")] public DateTimeOffset ClaimedAt { get; init; }
}

public sealed class LockClaimRequest
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("claim_key")] public string ClaimKey { get; init; } = string.Empty;
    [JsonPropertyName("chosen_value")] public string ChosenValue { get; init; } = string.Empty;
}

public sealed class LockClaimResponse
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("claim_key")] public string ClaimKey { get; init; } = string.Empty;
    [JsonPropertyName("chosen_value")] public string ChosenValue { get; init; } = string.Empty;
    [JsonPropertyName("locked_at")] public DateTimeOffset LockedAt { get; init; }
}

public sealed class ConflictDto
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;
    [JsonPropertyName("last_scored_at")] public DateTimeOffset LastScoredAt { get; init; }
}

public sealed class HydrateResponse
{
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; init; }
    [JsonPropertyName("claims_added")] public int ClaimsAdded { get; init; }
    [JsonPropertyName("stage1_claims")] public int Stage1Claims { get; init; }
    [JsonPropertyName("stage2_claims")] public int Stage2Claims { get; init; }
    [JsonPropertyName("needs_review")] public bool NeedsReview { get; init; }
    [JsonPropertyName("review_item_id")] public Guid? ReviewItemId { get; init; }
    [JsonPropertyName("success")] public bool Success { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
}

public sealed class MetadataOverrideRequest
{
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = [];
}

public sealed class MetadataOverrideResponse
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("fields_updated")] public int FieldsUpdated { get; init; }
    [JsonPropertyName("overridden_at")] public DateTimeOffset OverriddenAt { get; init; }
}

public sealed class ReclassifyRequest
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;
}

public sealed class ReclassifyResponse
{
    [JsonPropertyName("entity_id")] public Guid EntityId { get; init; }
    [JsonPropertyName("new_media_type")] public string NewMediaType { get; init; } = string.Empty;
    [JsonPropertyName("reclassified_at")] public DateTimeOffset ReclassifiedAt { get; init; }
    [JsonPropertyName("review_resolved")] public bool ReviewResolved { get; init; }
}

public sealed class LabelResolveRequest
{
    [JsonPropertyName("qids")]
    public IReadOnlyList<string> Qids { get; init; } = [];
}

public sealed class LabelResolveEntry
{
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("entity_type")] public string? EntityType { get; init; }
}
