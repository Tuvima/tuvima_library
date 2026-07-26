using System.Text.Json.Serialization;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Api.Models;

// -- GET /system/status ---------------------------------------------------------

// -- /admin/api-keys ------------------------------------------------------------

// -- /admin/provider-configs ----------------------------------------------------

// \u2500\u2500 GET /collections/{id}/related \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

/// <summary>
/// Response for GET /collections/{id}/related.
/// Includes the matched collections and the cascade reason that determined the section title.
/// </summary>
// \u2500\u2500 GET /collections \u2500\u2500----------------------------------------------------------------


/// <summary>
/// DTO for the GET /collections/parents endpoint — franchise-level parent collections (Universes).
/// Uses snake_case JSON names compatible with the Dashboard's CollectionRaw deserialiser.
/// </summary>
    /// <summary>Empty works list — parent collections aggregate through children, not direct works.</summary>



// -- PUT /settings/providers/{name} ---------------------------------------------

// -- GET /settings/providers ----------------------------------------------------

// -- POST /settings/providers/{name}/test --------------------------------------

// -- POST /settings/providers/{name}/sample -----------------------------------

// -- PUT /settings/providers/{name}/config ------------------------------------

// -- PUT /settings/providers/priority -----------------------------------------

// -- GET /metadata/claims/{entityId} ------------------------------------------

public sealed class ClaimDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("claim_value")]
    public string ClaimValue { get; init; } = string.Empty;

    [JsonPropertyName("provider_id")]
    public Guid ProviderId { get; init; }

    [JsonPropertyName("decision_source_provider_id")]
    public Guid? DecisionSourceProviderId { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("is_user_locked")]
    public bool IsUserLocked { get; init; }

    [JsonPropertyName("claimed_at")]
    public DateTimeOffset ClaimedAt { get; init; }

    public static ClaimDto FromDomain(Domain.Entities.MetadataClaim c) => new()
    {
        Id           = c.Id,
        ClaimKey     = c.ClaimKey,
        ClaimValue   = c.ClaimValue,
        ProviderId   = c.ProviderId,
        DecisionSourceProviderId = c.DecisionSourceProviderId,
        Confidence   = c.Confidence,
        IsUserLocked = c.IsUserLocked,
        ClaimedAt    = c.ClaimedAt,
    };
}

// -- PATCH /metadata/lock-claim -----------------------------------------------

public sealed class LockClaimRequest
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("chosen_value")]
    public string ChosenValue { get; init; } = string.Empty;
}

public sealed class LockClaimResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("chosen_value")]
    public string ChosenValue { get; init; } = string.Empty;

    [JsonPropertyName("locked_at")]
    public DateTimeOffset LockedAt { get; init; }
}

// -- DELETE /admin/api-keys (revoke all) --------------------------------------

// -- GET/PUT /settings/organization-template ----------------------------------

// -- GET /metadata/conflicts -------------------------------------------------

public sealed class ConflictDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("last_scored_at")]
    public DateTimeOffset LastScoredAt { get; init; }

    public static ConflictDto FromDomain(Domain.Entities.CanonicalValue cv) => new()
    {
        EntityId    = cv.EntityId,
        Key         = cv.Key,
        Value       = cv.Value,
        LastScoredAt = cv.LastScoredAt,
    };
}

// -- POST /metadata/hydrate/{entityId} ----------------------------------------

public sealed class HydrateResponse
{
    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("claims_added")]
    public int ClaimsAdded { get; init; }

    [JsonPropertyName("stage1_claims")]
    public int Stage1Claims { get; init; }

    [JsonPropertyName("stage2_claims")]
    public int Stage2Claims { get; init; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; init; }

    [JsonPropertyName("review_item_id")]
    public Guid? ReviewItemId { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

// -- PUT /metadata/{entityId}/override --------------------------------------

public sealed class MetadataOverrideRequest
{
    /// <summary>Map of claim keys to user-chosen values.</summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, string> Fields { get; init; } = new();
}

public sealed class MetadataOverrideResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("fields_updated")]
    public int FieldsUpdated { get; init; }

    [JsonPropertyName("overridden_at")]
    public DateTimeOffset OverriddenAt { get; init; }
}

// -- Review Queue DTOs --------------------------------------------------------

public sealed class ReviewItemDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("entity_type")]
    public string EntityType { get; init; } = string.Empty;

    [JsonPropertyName("trigger")]
    public string Trigger { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("proposed_collection_id")]
    public string? ProposedCollectionId { get; init; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; init; }

    [JsonPropertyName("candidates_json")]
    public string? CandidatesJson { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("resolved_at")]
    public DateTimeOffset? ResolvedAt { get; init; }

    [JsonPropertyName("resolved_by")]
    public string? ResolvedBy { get; init; }

    /// <summary>
    /// The media type of the entity (e.g. "Epub", "Audiobook"), populated
    /// from canonical values.
    /// </summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>
    /// Best-available display title for the entity (from canonical "title",
    /// falling back to "file_name" canonical, then detail string).
    /// </summary>
    [JsonPropertyName("entity_title")]
    public string? EntityTitle { get; init; }

    /// <summary>
    /// Cover art URL from canonical "cover" value, if available.
    /// </summary>
    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    /// <summary>
    /// Bridge identifiers extracted from canonical values (isbn, isbn_13, isbn_10,
    /// asin, apple_books_id, wikidata_qid, etc.). ISBN is shown prominently.
    /// </summary>
    [JsonPropertyName("bridge_identifiers")]
    public Dictionary<string, string> BridgeIdentifiers { get; init; } = [];

    public static ReviewItemDto FromDomain(
        Domain.Entities.ReviewQueueEntry e,
        string? mediaType = null,
        string? entityTitle = null,
        string? coverUrl = null,
        Dictionary<string, string>? bridgeIdentifiers = null) => new()
    {
        Id                 = e.Id,
        EntityId           = e.EntityId,
        EntityType         = e.EntityType,
        Trigger            = e.Trigger,
        Status             = e.Status,
        ProposedCollectionId      = e.ProposedCollectionId,
        ConfidenceScore    = e.ConfidenceScore,
        CandidatesJson     = e.CandidatesJson,
        Detail             = e.Detail,
        CreatedAt          = e.CreatedAt,
        ResolvedAt         = e.ResolvedAt,
        ResolvedBy         = e.ResolvedBy,
        MediaType          = mediaType,
        EntityTitle        = entityTitle,
        CoverUrl           = coverUrl,
        BridgeIdentifiers  = bridgeIdentifiers ?? [],
    };
}

public sealed class ReviewResolveRequest
{
    [JsonPropertyName("selected_qid")]
    public string? SelectedQid { get; init; }

    [JsonPropertyName("field_overrides")]
    public List<FieldOverrideDto>? FieldOverrides { get; init; }

    /// <summary>
    /// When resolving via search results, the provider that produced the
    /// selected match (e.g. "apple_books").
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; init; }

    /// <summary>
    /// The provider-specific item identifier for the selected match.
    /// Used to re-fetch full metadata from the provider.
    /// </summary>
    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }
}

public sealed class FieldOverrideDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; init; }
}

public sealed class ReviewCountResponse
{
    [JsonPropertyName("pending_count")]
    public int PendingCount { get; init; }
}

// -- POST /metadata/{entityId}/reclassify --------------------------------------

public sealed class ReclassifyRequest
{
    /// <summary>The new media type to assign (e.g. "Audiobooks", "Music", "Movies", "TV").</summary>
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;
}

public sealed class ReclassifyResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("new_media_type")]
    public string NewMediaType { get; init; } = string.Empty;

    [JsonPropertyName("reclassified_at")]
    public DateTimeOffset ReclassifiedAt { get; init; }

    [JsonPropertyName("review_resolved")]
    public bool ReviewResolved { get; init; }
}

// -- POST /metadata/labels/resolve ---------------------------------------------

public sealed class LabelResolveRequest
{
    [JsonPropertyName("qids")]
    public IReadOnlyList<string> Qids { get; init; } = [];
}

public sealed class LabelResolveEntry
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("entity_type")]
    public string? EntityType { get; init; }
}
