using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>
/// DTO for a single item within a managed hub (System List, Playlist, Mix).
/// Includes work metadata resolved from canonical values.
/// </summary>
public sealed class HubItemDto
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; init; }
}

/// <summary>DTO for a resolved hub item (from rule evaluation).</summary>
public sealed class HubResolvedItemDto
{
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = "";

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }
}
