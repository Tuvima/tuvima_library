using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

public sealed class SeriesManifestViewDto
{
    [JsonPropertyName("collection_id")] public Guid CollectionId { get; init; }
    [JsonPropertyName("series_qid")] public required string SeriesQid { get; init; }
    [JsonPropertyName("series_label")] public string? SeriesLabel { get; init; }
    [JsonPropertyName("last_hydrated_at")] public DateTimeOffset? LastHydratedAt { get; init; }
    [JsonPropertyName("total_count")] public int TotalCount { get; init; }
    [JsonPropertyName("owned_count")] public int OwnedCount { get; init; }
    [JsonPropertyName("missing_count")] public int MissingCount { get; init; }
    [JsonPropertyName("provisional_count")] public int ProvisionalCount { get; init; }
    [JsonPropertyName("ambiguous_count")] public int AmbiguousCount { get; init; }
    [JsonPropertyName("warnings")] public IReadOnlyList<SeriesManifestWarningDto> Warnings { get; init; } = [];
    [JsonPropertyName("items")] public IReadOnlyList<SeriesManifestItemDto> Items { get; init; } = [];
}

public sealed class SeriesManifestItemDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("item_qid")] public required string ItemQid { get; init; }
    [JsonPropertyName("item_label")] public string? ItemLabel { get; init; }
    [JsonPropertyName("item_description")] public string? ItemDescription { get; init; }
    [JsonPropertyName("media_type")] public string? MediaType { get; init; }
    [JsonPropertyName("raw_ordinal")] public string? RawOrdinal { get; init; }
    [JsonPropertyName("parsed_ordinal")] public double? ParsedOrdinal { get; init; }
    [JsonPropertyName("sort_order")] public double? SortOrder { get; init; }
    [JsonPropertyName("publication_date")] public string? PublicationDate { get; init; }
    [JsonPropertyName("parent_collection_qid")] public string? ParentCollectionQid { get; init; }
    [JsonPropertyName("parent_collection_label")] public string? ParentCollectionLabel { get; init; }
    [JsonPropertyName("is_collection")] public bool IsCollection { get; init; }
    [JsonPropertyName("is_expanded_from_collection")] public bool IsExpandedFromCollection { get; init; }
    [JsonPropertyName("order_source")] public required string OrderSource { get; init; }
    [JsonPropertyName("ownership_state")] public required string OwnershipState { get; init; }
    [JsonPropertyName("linked_work_id")] public Guid? LinkedWorkId { get; init; }
}

public sealed class SeriesManifestWarningDto
{
    [JsonPropertyName("code")] public required string Code { get; init; }
    [JsonPropertyName("message")] public required string Message { get; init; }
    [JsonPropertyName("qid")] public string? Qid { get; init; }
}
