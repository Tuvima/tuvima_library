using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class ActivityAuditQuery
{
    public string? Search { get; set; }
    public string? MediaType { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
    public string? EventType { get; set; }
    public DateTimeOffset? Start { get; set; }
    public DateTimeOffset? End { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 25;
    public string? Sort { get; set; }
    public string SortDirection { get; set; } = "desc";
}

public sealed class ActivityBatchSummaryViewModel
{
    [JsonPropertyName("batch_id")] public Guid BatchId { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; set; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; set; }
    [JsonPropertyName("last_activity_at")] public DateTimeOffset? LastActivityAt { get; set; }
    [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
    [JsonPropertyName("duration_label")] public string? DurationLabel { get; set; }
    [JsonPropertyName("media_type_count")] public int MediaTypeCount { get; set; }
    [JsonPropertyName("title_count")] public int TitleCount { get; set; }
    [JsonPropertyName("item_count")] public int ItemCount { get; set; }
    [JsonPropertyName("event_count")] public int EventCount { get; set; }
    [JsonPropertyName("people_count")] public int PeopleCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("alert_count")] public int AlertCount { get; set; }
    [JsonPropertyName("media_types")] public List<ActivityMediaTypeCountViewModel> MediaTypes { get; set; } = [];
}

public sealed class ActivityMediaTypeGroupViewModel
{
    [JsonPropertyName("batch_id")] public Guid BatchId { get; set; }
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "Unknown";
    [JsonPropertyName("title_count")] public int TitleCount { get; set; }
    [JsonPropertyName("item_count")] public int ItemCount { get; set; }
    [JsonPropertyName("event_count")] public int EventCount { get; set; }
    [JsonPropertyName("people_count")] public int PeopleCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("alert_count")] public int AlertCount { get; set; }
    [JsonPropertyName("last_activity_at")] public DateTimeOffset? LastActivityAt { get; set; }
}

public sealed class ActivityMediaTypeCountViewModel
{
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "Unknown";
    [JsonPropertyName("count")] public int Count { get; set; }
}

public sealed class ActivityBatchItemViewModel
{
    [JsonPropertyName("batch_id")] public Guid BatchId { get; set; }
    [JsonPropertyName("asset_id")] public Guid? AssetId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "Unknown title";
    [JsonPropertyName("subtitle")] public string? Subtitle { get; set; }
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "Unknown";
    [JsonPropertyName("source_path")] public string? SourcePath { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "";
    [JsonPropertyName("processing_status")] public string ProcessingStatus { get; set; } = "";
    [JsonPropertyName("audit_status")] public string AuditStatus { get; set; } = "Complete";
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("provider")] public string? Provider { get; set; }
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; set; }
    [JsonPropertyName("cover_url")] public string? CoverUrl { get; set; }
    [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
    [JsonPropertyName("duration_label")] public string? DurationLabel { get; set; }
    [JsonPropertyName("library_entity_type")] public string? LibraryEntityType { get; set; }
    [JsonPropertyName("library_entity_id")] public Guid? LibraryEntityId { get; set; }
    [JsonPropertyName("people_count")] public int PeopleCount { get; set; }
    [JsonPropertyName("artwork_count")] public int ArtworkCount { get; set; }
    [JsonPropertyName("review_count")] public int ReviewCount { get; set; }
    [JsonPropertyName("alert_count")] public int AlertCount { get; set; }
    [JsonPropertyName("event_count")] public int EventCount { get; set; }
    [JsonPropertyName("last_activity_at")] public DateTimeOffset? LastActivityAt { get; set; }
}

public sealed class ActivityBatchItemDetailViewModel
{
    [JsonPropertyName("batch_id")] public Guid BatchId { get; set; }
    [JsonPropertyName("asset_id")] public Guid AssetId { get; set; }
    [JsonPropertyName("title")] public string Title { get; set; } = "Unknown title";
    [JsonPropertyName("media_type")] public string MediaType { get; set; } = "Unknown";
    [JsonPropertyName("source_path")] public string? SourcePath { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("processing_status")] public string? ProcessingStatus { get; set; }
    [JsonPropertyName("audit_status")] public string? AuditStatus { get; set; }
    [JsonPropertyName("stage")] public string? Stage { get; set; }
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; set; }
    [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; set; }
    [JsonPropertyName("duration_label")] public string? DurationLabel { get; set; }
    [JsonPropertyName("library_entity_type")] public string? LibraryEntityType { get; set; }
    [JsonPropertyName("library_entity_id")] public Guid? LibraryEntityId { get; set; }
    [JsonPropertyName("file_details")] public List<ActivityDetailFieldViewModel> FileDetails { get; set; } = [];
    [JsonPropertyName("timeline")] public List<ActivityTimelineEventViewModel> Timeline { get; set; } = [];
    [JsonPropertyName("people")] public List<ActivityPersonAuditViewModel> People { get; set; } = [];
    [JsonPropertyName("evidence")] public List<ActivityEvidenceViewModel> Evidence { get; set; } = [];
}

public sealed class ActivityTimelineEventViewModel
{
    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }
    [JsonPropertyName("event_type")] public string EventType { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("tone")] public string Tone { get; set; } = "neutral";
}

public sealed class ActivityPersonAuditViewModel
{
    [JsonPropertyName("person_id")] public Guid PersonId { get; set; }
    [JsonPropertyName("person_name")] public string PersonName { get; set; } = "";
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("wikidata_qid")] public string? WikidataQid { get; set; }
    [JsonPropertyName("batch_id")] public Guid? BatchId { get; set; }
    [JsonPropertyName("batch_started_at")] public DateTimeOffset? BatchStartedAt { get; set; }
    [JsonPropertyName("asset_id")] public Guid? AssetId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("media_type")] public string? MediaType { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; } = "Inferred from library link";
    [JsonPropertyName("provider_id")] public string? ProviderId { get; set; }
    [JsonPropertyName("hydrated_at")] public DateTimeOffset? HydratedAt { get; set; }
    [JsonPropertyName("headshot_status")] public string HeadshotStatus { get; set; } = "Unknown";
    [JsonPropertyName("headshot_url")] public string? HeadshotUrl { get; set; }

    public string PersonUrl => $"/details/person/{PersonId:D}";
}

public sealed class ActivityEvidenceViewModel
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string? Value { get; set; }
    [JsonPropertyName("provider_id")] public string? ProviderId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
    [JsonPropertyName("occurred_at")] public DateTimeOffset? OccurredAt { get; set; }
}

public sealed class ActivityDetailFieldViewModel
{
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("value")] public string? Value { get; set; }
}
