using System.Text.Json.Serialization;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Ingestion.Contracts;

namespace MediaEngine.Api.Models;

// ── GET /system/status ─────────────────────────────────────────────────────────

public sealed class SystemStatusResponse
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;
}

// ── /admin/api-keys ────────────────────────────────────────────────────────────

public sealed class ApiKeyDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "Administrator";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    public static ApiKeyDto FromDomain(ApiKey key) => new()
    {
        Id        = key.Id,
        Label     = key.Label,
        Role      = key.Role,
        CreatedAt = key.CreatedAt,
    };
}

public sealed class CreateApiKeyRequest
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Authorization role for this key.  Defaults to Administrator if omitted.
    /// Valid values: Administrator, Curator, Consumer.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

public sealed class CreateApiKeyResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "Administrator";

    /// <summary>
    /// The API key plaintext. Shown exactly once — store it now; it cannot be retrieved again.
    /// </summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

// ── /admin/provider-configs ────────────────────────────────────────────────────

public sealed class ProviderConfigDto
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    /// <summary>Secret values are returned as '********'; non-secret values are returned as-is.</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("is_secret")]
    public bool IsSecret { get; init; }

    public static ProviderConfigDto FromDomain(ProviderConfiguration cfg) => new()
    {
        ProviderId = cfg.ProviderId,
        Key        = cfg.Key,
        Value      = cfg.Value,
        IsSecret   = cfg.IsSecret,
    };
}

public sealed class UpsertProviderConfigRequest
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("is_secret")]
    public bool IsSecret { get; init; }
}

// ── GET /hubs/search ───────────────────────────────────────────────────────────

/// <summary>
/// A single work result from the hub search endpoint.
/// Carries enough information to render a command-palette result row:
/// the work's own title, the Hub it belongs to, and its media type for icon selection.
/// </summary>
public sealed class SearchResultDto
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("hub_id")]
    public Guid? HubId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("hub_display_name")]
    public string HubDisplayName { get; init; } = string.Empty;
}


// \u2500\u2500 GET /hubs/{id}/related \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500

/// <summary>
/// Response for GET /hubs/{id}/related.
/// Includes the matched hubs and the cascade reason that determined the section title.
/// </summary>
public sealed class RelatedHubsResponse
{
    [JsonPropertyName("section_title")]
    public string SectionTitle { get; init; } = string.Empty;

    /// <summary>"series" | "author" | "genre" | "explore"</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("hubs")]
    public List<HubDto> Hubs { get; init; } = [];
}
// \u2500\u2500 GET /hubs \u2500\u2500────────────────────────────────────────────────────────────────

public sealed class HubDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("universe_id")]
    public Guid? UniverseId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("works")]
    public List<WorkDto> Works { get; init; } = [];

    public static HubDto FromDomain(Hub hub) => new()
    {
        Id         = hub.Id,
        UniverseId = hub.UniverseId,
        CreatedAt  = hub.CreatedAt,
        Works      = hub.Works.Select(WorkDto.FromDomain).ToList(),
    };
}

public sealed class WorkDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("hub_id")]
    public Guid? HubId { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("sequence_index")]
    public int? SequenceIndex { get; init; }

    [JsonPropertyName("universe_mismatch")]
    public bool UniverseMismatch { get; init; }

    [JsonPropertyName("universe_mismatch_at")]
    public DateTimeOffset? UniverseMismatchAt { get; init; }

    [JsonPropertyName("canonical_values")]
    public List<CanonicalValueDto> CanonicalValues { get; init; } = [];

    public static WorkDto FromDomain(Work work) => new()
    {
        Id                 = work.Id,
        HubId              = work.HubId,
        MediaType          = work.MediaType.ToString(),
        SequenceIndex      = work.SequenceIndex,
        UniverseMismatch   = work.UniverseMismatch,
        UniverseMismatchAt = work.UniverseMismatchAt,
        CanonicalValues    = work.CanonicalValues.Select(CanonicalValueDto.FromDomain).ToList(),
    };
}

public sealed class CanonicalValueDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("last_scored_at")]
    public DateTimeOffset LastScoredAt { get; init; }

    public static CanonicalValueDto FromDomain(CanonicalValue cv) => new()
    {
        Key          = cv.Key,
        Value        = cv.Value,
        LastScoredAt = cv.LastScoredAt,
    };
}

// ── POST /ingestion/scan ───────────────────────────────────────────────────────

public sealed class ScanRequest
{
    /// <summary>
    /// Optional root path to scan. When absent, the engine uses the configured
    /// WatchDirectory from IngestionOptions.
    /// </summary>
    [JsonPropertyName("root_path")]
    public string? RootPath { get; init; }
}

public sealed class ScanResponse
{
    [JsonPropertyName("operations")]
    public List<PendingOperationDto> Operations { get; init; } = [];

    [JsonPropertyName("total_count")]
    public int TotalCount => Operations.Count;
}

public sealed class PendingOperationDto
{
    [JsonPropertyName("source_path")]
    public string SourcePath { get; init; } = string.Empty;

    [JsonPropertyName("destination_path")]
    public string DestinationPath { get; init; } = string.Empty;

    [JsonPropertyName("operation_kind")]
    public string OperationKind { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }

    public static PendingOperationDto FromDomain(PendingOperation op) => new()
    {
        SourcePath      = op.SourcePath,
        DestinationPath = op.DestinationPath,
        OperationKind   = op.OperationKind,
        Reason          = op.Reason,
    };
}

// ── POST /ingestion/library-scan ───────────────────────────────────────────────

public sealed class LibraryScanResponse
{
    /// <summary>Number of Hub records created or updated in the database.</summary>
    [JsonPropertyName("hubs_upserted")]
    public int HubsUpserted { get; init; }

    /// <summary>Number of Edition/MediaAsset canonical value sets upserted.</summary>
    [JsonPropertyName("editions_upserted")]
    public int EditionsUpserted { get; init; }

    /// <summary>Number of Person records recovered from .people/ person.xml sidecars.</summary>
    [JsonPropertyName("people_recovered")]
    public int PeopleRecovered { get; init; }

    /// <summary>Number of narrative universe records recovered from .universe/ sidecars.</summary>
    [JsonPropertyName("universes_upserted")]
    public int UniversesUpserted { get; init; }

    /// <summary>Number of fictional entity records recovered from .universe/ sidecars.</summary>
    [JsonPropertyName("entities_upserted")]
    public int EntitiesUpserted { get; init; }

    /// <summary>Number of relationship edges recovered from .universe/ sidecars.</summary>
    [JsonPropertyName("relationships_upserted")]
    public int RelationshipsUpserted { get; init; }

    /// <summary>Number of sidecar files that could not be parsed or hydrated.</summary>
    [JsonPropertyName("errors")]
    public int Errors { get; init; }

    /// <summary>Wall-clock time taken for the full scan, in milliseconds.</summary>
    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; init; }
}

// ── PATCH /metadata/resolve ────────────────────────────────────────────────────

public sealed class ResolveRequest
{
    /// <summary>The Work or Edition entity whose canonical value is being overridden.</summary>
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    /// <summary>The metadata field key, e.g. "title", "release_year".</summary>
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    /// <summary>The human-chosen winning value to persist.</summary>
    [JsonPropertyName("chosen_value")]
    public string ChosenValue { get; init; } = string.Empty;
}

public sealed class ResolveResponse
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("chosen_value")]
    public string ChosenValue { get; init; } = string.Empty;

    [JsonPropertyName("resolved_at")]
    public DateTimeOffset ResolvedAt { get; init; }
}

// ── GET /settings/folders ──────────────────────────────────────────────────────

public sealed class FolderSettingsResponse
{
    [JsonPropertyName("watch_directory")]
    public string WatchDirectory { get; init; } = string.Empty;

    [JsonPropertyName("library_root")]
    public string LibraryRoot { get; init; } = string.Empty;

    [JsonPropertyName("staging_directory")]
    public string StagingDirectory { get; init; } = string.Empty;
}

public sealed class UpdateFoldersRequest
{
    [JsonPropertyName("watch_directory")]
    public string? WatchDirectory { get; init; }

    [JsonPropertyName("library_root")]
    public string? LibraryRoot { get; init; }

    [JsonPropertyName("staging_directory")]
    public string? StagingDirectory { get; init; }
}

// ── GET /settings/server-general ──────────────────────────────────────────────

public sealed class ServerGeneralResponse
{
    [JsonPropertyName("server_name")]
    public string ServerName { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("country")]
    public string Country { get; init; } = "US";

    [JsonPropertyName("date_format")]
    public string DateFormat { get; init; } = "system";

    [JsonPropertyName("time_format")]
    public string TimeFormat { get; init; } = "system";
}

public sealed class ServerGeneralRequest
{
    [JsonPropertyName("server_name")]
    public string ServerName { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("country")]
    public string Country { get; init; } = "US";

    [JsonPropertyName("date_format")]
    public string DateFormat { get; init; } = "system";

    [JsonPropertyName("time_format")]
    public string TimeFormat { get; init; } = "system";
}

// ── POST /settings/test-path ───────────────────────────────────────────────────

public sealed class TestPathRequest
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}

public sealed class TestPathResponse
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("exists")]
    public bool Exists { get; init; }

    [JsonPropertyName("has_read")]
    public bool HasRead { get; init; }

    [JsonPropertyName("has_write")]
    public bool HasWrite { get; init; }
}

// ── POST /settings/browse-directory ────────────────────────────────────────────

public sealed class BrowseDirectoryRequest
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }
}

public sealed class BrowseDirectoryResponse
{
    [JsonPropertyName("current_path")]
    public string CurrentPath { get; init; } = string.Empty;

    [JsonPropertyName("parent_path")]
    public string? ParentPath { get; init; }

    [JsonPropertyName("directories")]
    public List<string> Directories { get; init; } = [];
}

// ── PUT /settings/providers/{name} ─────────────────────────────────────────────

public sealed class UpdateProviderRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }
}

// ── GET /settings/providers ────────────────────────────────────────────────────

public sealed class ProviderStatusResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("is_zero_key")]
    public bool IsZeroKey { get; init; }

    [JsonPropertyName("is_reachable")]
    public bool IsReachable { get; init; }

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("capability_tags")]
    public List<string> CapabilityTags { get; init; } = [];

    [JsonPropertyName("default_weight")]
    public double DefaultWeight { get; init; }

    [JsonPropertyName("field_weights")]
    public Dictionary<string, double> FieldWeights { get; init; } = [];

    [JsonPropertyName("hydration_stages")]
    public List<int> HydrationStages { get; init; } = [1];

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; init; } = [];

    [JsonPropertyName("field_mappings")]
    public List<FieldMappingResponse>? FieldMappings { get; init; }

    [JsonPropertyName("throttle_ms")]
    public int ThrottleMs { get; init; }

    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; init; } = 1;

    [JsonPropertyName("available_fields")]
    public List<string> AvailableFields { get; init; } = [];

    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; init; } = [];

    [JsonPropertyName("requires_api_key")]
    public bool RequiresApiKey { get; init; }

    [JsonPropertyName("has_api_key")]
    public bool HasApiKey { get; init; }

    [JsonPropertyName("api_key_delivery")]
    public string? ApiKeyDelivery { get; init; }

    [JsonPropertyName("api_key_param_name")]
    public string? ApiKeyParamName { get; init; }

    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; init; } = 10;

    /// <summary>Optional Material icon name override (e.g. "MenuBook"). Null = use default accent icon.</summary>
    [JsonPropertyName("custom_icon_name")]
    public string? CustomIconName { get; init; }
}

/// <summary>Field mapping entry for provider status response.</summary>
public sealed class FieldMappingResponse
{
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("json_path")]
    public string JsonPath { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("transform")]
    public string? Transform { get; init; }
}

// ── POST /settings/providers/{name}/test ──────────────────────────────────────

public sealed class ProviderTestResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("response_time_ms")]
    public int ResponseTimeMs { get; init; }

    [JsonPropertyName("sample_fields")]
    public List<string> SampleFields { get; init; } = [];

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

// ── POST /settings/providers/{name}/sample ───────────────────────────────────

public sealed class ProviderSampleRequest
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; init; }

    [JsonPropertyName("asin")]
    public string? Asin { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }
}

public sealed class ProviderSampleResponse
{
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("claims")]
    public List<ProviderSampleClaim> Claims { get; init; } = [];
}

public sealed class ProviderSampleClaim
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

// ── PUT /settings/providers/{name}/config ────────────────────────────────────

public sealed class ProviderConfigUpdateRequest
{
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    [JsonPropertyName("weight")]
    public double? Weight { get; init; }

    [JsonPropertyName("field_weights")]
    public Dictionary<string, double>? FieldWeights { get; init; }

    [JsonPropertyName("capability_tags")]
    public List<string>? CapabilityTags { get; init; }

    [JsonPropertyName("endpoints")]
    public Dictionary<string, string>? Endpoints { get; init; }

    [JsonPropertyName("throttle_ms")]
    public int? ThrottleMs { get; init; }

    [JsonPropertyName("max_concurrency")]
    public int? MaxConcurrency { get; init; }

    [JsonPropertyName("field_mappings")]
    public List<FieldMappingUpdateDto>? FieldMappings { get; init; }

    /// <summary>Timeout in seconds for HTTP requests. Null = no change.</summary>
    [JsonPropertyName("timeout_seconds")]
    public int? TimeoutSeconds { get; init; }

    /// <summary>API key for providers that require authentication. Null = no change; empty = clear.</summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    /// <summary>Material icon name override (e.g. "MenuBook"). Null = no change; empty string = clear override.</summary>
    [JsonPropertyName("custom_icon_name")]
    public string? CustomIconName { get; init; }
}

/// <summary>Field mapping entry in a provider config update.</summary>
public sealed class FieldMappingUpdateDto
{
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; init; } = string.Empty;

    [JsonPropertyName("json_path")]
    public string JsonPath { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5;

    [JsonPropertyName("transform")]
    public string? Transform { get; init; }

    [JsonPropertyName("transform_args")]
    public string? TransformArgs { get; init; }
}

// ── PUT /settings/providers/priority ─────────────────────────────────────────

public sealed class ProviderPriorityRequest
{
    [JsonPropertyName("order")]
    public List<string> Order { get; init; } = [];
}

// ── /profiles ────────────────────────────────────────────────────────────────

public sealed class ProfileResponseDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("avatar_color")]
    public string AvatarColor { get; init; } = "#7C4DFF";

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("navigation_config")]
    public string? NavigationConfig { get; init; }

    public static ProfileResponseDto FromDomain(Domain.Aggregates.Profile p) => new()
    {
        Id               = p.Id,
        DisplayName      = p.DisplayName,
        AvatarColor      = p.AvatarColor,
        Role             = p.Role.ToString(),
        CreatedAt        = p.CreatedAt,
        NavigationConfig = p.NavigationConfig,
    };
}

public sealed class CreateProfileRequest
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = "Consumer";

    [JsonPropertyName("avatar_color")]
    public string AvatarColor { get; init; } = "#7C4DFF";

    [JsonPropertyName("navigation_config")]
    public string? NavigationConfig { get; init; }
}

public sealed class UpdateProfileRequest
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("avatar_color")]
    public string AvatarColor { get; init; } = string.Empty;

    [JsonPropertyName("navigation_config")]
    public string? NavigationConfig { get; init; }
}

// ── GET /metadata/claims/{entityId} ──────────────────────────────────────────

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
        Confidence   = c.Confidence,
        IsUserLocked = c.IsUserLocked,
        ClaimedAt    = c.ClaimedAt,
    };
}

// ── PATCH /metadata/lock-claim ───────────────────────────────────────────────

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

// ── DELETE /admin/api-keys (revoke all) ──────────────────────────────────────

public sealed class RevokeAllKeysResponse
{
    [JsonPropertyName("revoked_count")]
    public int RevokedCount { get; init; }
}

// ── GET/PUT /settings/organization-template ──────────────────────────────────

public sealed class OrganizationTemplateResponse
{
    [JsonPropertyName("template")]
    public string Template { get; init; } = string.Empty;

    /// <summary>Sample resolved path using representative token values.</summary>
    [JsonPropertyName("preview")]
    public string? Preview { get; init; }

    /// <summary>Per-media-type templates. Keys are media type names or "default".</summary>
    [JsonPropertyName("templates")]
    public Dictionary<string, string> Templates { get; init; } = new();
}

public sealed class UpdateOrganizationTemplateRequest
{
    [JsonPropertyName("template")]
    public string Template { get; init; } = string.Empty;

    /// <summary>Per-media-type templates. Keys are media type names or "default".</summary>
    [JsonPropertyName("templates")]
    public Dictionary<string, string>? Templates { get; init; }
}

// ── GET /metadata/conflicts ─────────────────────────────────────────────────

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

// ── POST /metadata/hydrate/{entityId} ────────────────────────────────────────

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

    [JsonPropertyName("stage3_claims")]
    public int Stage3Claims { get; init; }

    [JsonPropertyName("needs_review")]
    public bool NeedsReview { get; init; }

    [JsonPropertyName("review_item_id")]
    public Guid? ReviewItemId { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

// ── PUT /metadata/{entityId}/override ──────────────────────────────────────

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

// ── Review Queue DTOs ────────────────────────────────────────────────────────

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

    [JsonPropertyName("proposed_hub_id")]
    public string? ProposedHubId { get; init; }

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

    public static ReviewItemDto FromDomain(
        Domain.Entities.ReviewQueueEntry e,
        string? mediaType = null,
        string? entityTitle = null) => new()
    {
        Id              = e.Id,
        EntityId        = e.EntityId,
        EntityType      = e.EntityType,
        Trigger         = e.Trigger,
        Status          = e.Status,
        ProposedHubId   = e.ProposedHubId,
        ConfidenceScore = e.ConfidenceScore,
        CandidatesJson  = e.CandidatesJson,
        Detail          = e.Detail,
        CreatedAt       = e.CreatedAt,
        ResolvedAt      = e.ResolvedAt,
        ResolvedBy      = e.ResolvedBy,
        MediaType       = mediaType,
        EntityTitle     = entityTitle,
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

// ── /ingestion/watch-folder ──────────────────────────────────────────────────

public sealed class WatchFolderResponse
{
    [JsonPropertyName("watch_directory")]
    public string? WatchDirectory { get; init; }

    [JsonPropertyName("files")]
    public List<WatchFolderFileDto> Files { get; init; } = [];
}

public sealed class WatchFolderFileDto
{
    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = string.Empty;

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; init; } = string.Empty;

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; init; }

    [JsonPropertyName("last_modified")]
    public DateTimeOffset LastModified { get; init; }
}

// ── POST /metadata/{entityId}/reclassify ──────────────────────────────────────

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

// ── POST /metadata/labels/resolve ─────────────────────────────────────────────

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
