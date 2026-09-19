using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Universe;

/// <summary>Discriminated shared-editor target. Universe uses its authoritative narrative-root QID; fictional entities use their internal ID plus QID.</summary>
public sealed record SharedEntityEditorTargetDto(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("universe_qid")] string UniverseQid,
    [property: JsonPropertyName("fictional_entity_id")] Guid? FictionalEntityId,
    [property: JsonPropertyName("qid")] string? Qid);

/// <summary>Stable target discriminators understood by all shared-editor clients.</summary>
public static class SharedEntityEditorTargetKinds
{
    public const string Universe = "Universe";
    public const string FictionalEntity = "FictionalEntity";
}

/// <summary>Stable graph-editor section identifiers. Graph targets deliberately have no Files tab.</summary>
public static class SharedEntityEditorSections
{
    public const string Details = "details";
    public const string Artwork = "artwork";
    public const string Appearances = "appearances";
    public const string Relationships = "relationships";
    public const string Timeline = "timeline";
    public const string Sources = "sources";
    public const string History = "history";
    public const string Enrichment = "enrichment";
}

public sealed record SharedEntityEditorCapabilityDto(string section, bool readable, bool editable, string? read_only_hint = null);
public sealed record SharedEntityEditorContextDto(
    SharedEntityEditorTargetDto target,
    string label,
    string category,
    IReadOnlyList<SharedEntityEditorCapabilityDto> capabilities,
    string enrichment_status);
public sealed record SharedEntityCategorySummaryDto(string category, string label, int count);
public sealed record SharedEntitySelectorItemDto(Guid id, string qid, string label, string category, string? description);
public sealed record SharedEntitySelectorPageDto(IReadOnlyList<SharedEntitySelectorItemDto> items, int offset, int limit, int total, bool has_more);
public sealed record SharedEntityDetailsDto(Guid? id, string qid, string label, string category, string? description, string universe_qid, string? universe_label);
public sealed record SharedEntityDetailsUpdateRequest(string label, string? description);
public sealed record SharedEntityArtworkDto(Guid id, string asset_type, string? image_url, bool is_preferred, bool is_user_override, string? source_provider);
public sealed record SharedEntityArtworkUpdateRequest(string asset_type, string image_url, bool preferred = true);
public sealed record SharedEntityAppearanceDto(string work_qid, string? work_label, string link_type, string? role, string? work_context, string? anchor_kind, string? anchor_value, string? narrative_time_index, string? start_time, string? end_time, string? spoiler_for_work_qid, string provenance);
public sealed record SharedEntityRelationshipDto(string statement_key, string subject_qid, string type, string object_qid, string provenance, string? work_qid, IReadOnlyList<UniverseGraphQualifierDto> qualifiers);
public sealed record SharedEntityTimelineEntryDto(string kind, string value, string? start_time, string? end_time, string? work_qid, string provenance);
public sealed record SharedEntitySourceDto(string kind, string value, string provenance, string? source_provider, string? work_qid);
public sealed record SharedEntityHistoryEntryDto(Guid id, string event_type, DateTimeOffset occurred_at, string? detail);
public sealed record SharedEntityEnrichmentStatusDto(string status, DateTimeOffset? enriched_at, long? wikidata_revision_id);
public sealed record SharedEntityRefreshDto(bool queued, string message);
