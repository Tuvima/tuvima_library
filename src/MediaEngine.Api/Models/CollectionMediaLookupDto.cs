using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

public sealed class CollectionMediaLookupDto
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("artwork_url")]
    public string? ArtworkUrl { get; init; }

    [JsonPropertyName("parent_context")]
    public string? ParentContext { get; init; }

    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("already_in_collection")]
    public bool AlreadyInCollection { get; init; }
}
