using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Collections;

public sealed class ContributorShelfDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("person_id")]
    public Guid PersonId { get; init; }

    [JsonPropertyName("person_name")]
    public string PersonName { get; init; } = string.Empty;

    [JsonPropertyName("headshot_url")]
    public string? HeadshotUrl { get; init; }

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("lane")]
    public string Lane { get; init; } = string.Empty;

    [JsonPropertyName("shelf_type")]
    public string ShelfType { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("owned_count")]
    public int OwnedCount { get; init; }

    [JsonPropertyName("earliest_year")]
    public int? EarliestYear { get; init; }

    [JsonPropertyName("latest_year")]
    public int? LatestYear { get; init; }

    [JsonPropertyName("items")]
    public IReadOnlyList<ContributorShelfItemDto> Items { get; init; } = [];
}

public sealed class ContributorShelfItemDto
{
    [JsonPropertyName("work_id")]
    public Guid WorkId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonPropertyName("cover_url")]
    public string? CoverUrl { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }
}
