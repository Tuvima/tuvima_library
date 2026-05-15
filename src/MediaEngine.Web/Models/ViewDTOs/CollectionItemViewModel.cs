using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// Dashboard view model for a single item within a managed collection.
/// </summary>
public sealed class CollectionItemViewModel
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("work_id")]
    public Guid WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; set; }

    [JsonPropertyName("sort_order")]
    public int SortOrder { get; set; }
}

public sealed class CollectionMediaLookupItemViewModel
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; set; }

    [JsonPropertyName("creator")]
    public string? Creator { get; set; }

    [JsonPropertyName("media_type")]
    public string MediaType { get; set; } = string.Empty;

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("artwork_url")]
    public string? ArtworkUrl { get; set; }

    [JsonPropertyName("parent_context")]
    public string? ParentContext { get; set; }

    [JsonPropertyName("route")]
    public string? Route { get; set; }

    [JsonPropertyName("already_in_collection")]
    public bool AlreadyInCollection { get; set; }
}
