using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class ActivityBatchSummaryDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("last_activity_at")]
    public DateTimeOffset? LastActivityAt { get; init; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    [JsonPropertyName("duration_label")]
    public string? DurationLabel { get; init; }

    [JsonPropertyName("media_type_count")]
    public int MediaTypeCount { get; set; }

    [JsonPropertyName("title_count")]
    public int TitleCount { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("event_count")]
    public int EventCount { get; init; }

    [JsonPropertyName("people_count")]
    public int PeopleCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("alert_count")]
    public int AlertCount { get; init; }

    [JsonPropertyName("media_types")]
    public List<ActivityMediaTypeCountDto> MediaTypes { get; init; } = [];
}

public sealed class ActivityMediaTypeGroupDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "Unknown";

    [JsonPropertyName("title_count")]
    public int TitleCount { get; init; }

    [JsonPropertyName("item_count")]
    public int ItemCount { get; init; }

    [JsonPropertyName("event_count")]
    public int EventCount { get; init; }

    [JsonPropertyName("people_count")]
    public int PeopleCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("alert_count")]
    public int AlertCount { get; init; }

    [JsonPropertyName("last_activity_at")]
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed class ActivityMediaTypeCountDto
{
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "Unknown";

    [JsonPropertyName("count")]
    public int Count { get; init; }
}

public sealed class ActivityBatchItemDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid? AssetId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "Unknown title";

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "Unknown";

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("processing_status")]
    public string ProcessingStatus { get; set; } = "";

    [JsonPropertyName("audit_status")]
    public string AuditStatus { get; set; } = "Complete";

    [JsonPropertyName("stage")]
    public string? Stage { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; set; }

    [JsonPropertyName("duration_label")]
    public string? DurationLabel { get; set; }

    [JsonPropertyName("library_entity_type")]
    public string? LibraryEntityType { get; init; }

    [JsonPropertyName("library_entity_id")]
    public Guid? LibraryEntityId { get; init; }

    [JsonIgnore]
    public Guid? CoverAssetId { get; set; }

    [JsonPropertyName("people_count")]
    public int PeopleCount { get; init; }

    [JsonPropertyName("artwork_count")]
    public int ArtworkCount { get; init; }

    [JsonPropertyName("review_count")]
    public int ReviewCount { get; init; }

    [JsonPropertyName("alert_count")]
    public int AlertCount { get; init; }

    [JsonPropertyName("event_count")]
    public int EventCount { get; init; }

    [JsonPropertyName("last_activity_at")]
    public DateTimeOffset? LastActivityAt { get; init; }
}

public sealed class ActivityBatchItemDetailDto
{
    [JsonPropertyName("batch_id")]
    public Guid BatchId { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid AssetId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "Unknown title";

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "Unknown";

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("processing_status")]
    public string? ProcessingStatus { get; init; }

    [JsonPropertyName("audit_status")]
    public string? AuditStatus { get; init; }

    [JsonPropertyName("stage")]
    public string? Stage { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("duration_seconds")]
    public double? DurationSeconds { get; init; }

    [JsonPropertyName("duration_label")]
    public string? DurationLabel { get; init; }

    [JsonPropertyName("library_entity_type")]
    public string? LibraryEntityType { get; init; }

    [JsonPropertyName("library_entity_id")]
    public Guid? LibraryEntityId { get; init; }

    [JsonPropertyName("file_details")]
    public List<ActivityDetailFieldDto> FileDetails { get; init; } = [];

    [JsonPropertyName("timeline")]
    public List<ActivityTimelineEventDto> Timeline { get; init; } = [];

    [JsonPropertyName("people")]
    public List<ActivityPersonAuditDto> People { get; init; } = [];

    [JsonPropertyName("evidence")]
    public List<ActivityEvidenceDto> Evidence { get; init; } = [];
}

public sealed class ActivityTimelineEventDto
{
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("tone")]
    public string Tone { get; init; } = "neutral";
}

public sealed class ActivityPersonAuditDto
{
    [JsonPropertyName("person_id")]
    public Guid PersonId { get; init; }

    [JsonPropertyName("person_name")]
    public string PersonName { get; init; } = "";

    [JsonPropertyName("role")]
    public string? Role { get; init; }

    [JsonPropertyName("wikidata_qid")]
    public string? WikidataQid { get; init; }

    [JsonPropertyName("batch_id")]
    public Guid? BatchId { get; init; }

    [JsonPropertyName("batch_started_at")]
    public DateTimeOffset? BatchStartedAt { get; init; }

    [JsonPropertyName("asset_id")]
    public Guid? AssetId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "Inferred from library link";

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; init; }

    [JsonPropertyName("hydrated_at")]
    public DateTimeOffset? HydratedAt { get; init; }

    [JsonPropertyName("headshot_status")]
    public string HeadshotStatus { get; init; } = "Unknown";

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; set; }
}

public sealed class ActivityEvidenceDto
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }
}

public sealed class ActivityDetailFieldDto
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("value")]
    public string? Value { get; init; }
}
