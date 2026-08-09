using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

public sealed class LibraryItemTarget
{
    public Guid AssetId { get; init; }
    public Guid WorkId { get; init; }
    public string? FilePath { get; init; }
    public string? Title { get; init; }
    public string? MediaType { get; init; }
}

public sealed record LibraryItemRemovalTarget(
    Guid WorkId,
    Guid? CollectionId,
    Guid? ParentWorkId,
    IReadOnlyList<string> FilePaths,
    IReadOnlyList<string> ManagedAssetPaths,
    string? Title);

public sealed record LibraryItemRecoveryResult(Guid WorkId, Guid? AssetId, Guid? ReviewId);

public sealed record LibraryItemProvisionalResult(Guid WorkId, Guid? AssetId, int ClaimsWritten);

public sealed record LibraryItemHistoryEntry(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("entity_id")] Guid EntityId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("detail")] string? Detail,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("actor_label")] string ActorLabel);

public sealed class LibraryItemProvisionalMetadata
{
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("narrator")]
    public string? Narrator { get; init; }

    [JsonPropertyName("isbn")]
    public string? Isbn { get; init; }

    [JsonPropertyName("director")]
    public string? Director { get; init; }

    [JsonPropertyName("runtime")]
    public string? Runtime { get; init; }

    [JsonPropertyName("seasons")]
    public string? Seasons { get; init; }

    [JsonPropertyName("track_count")]
    public string? TrackCount { get; init; }

    [JsonPropertyName("host")]
    public string? Host { get; init; }

    [JsonPropertyName("writer")]
    public string? Writer { get; init; }

    [JsonPropertyName("artist")]
    public string? Artist { get; init; }

    [JsonPropertyName("page_count")]
    public string? PageCount { get; init; }
}
