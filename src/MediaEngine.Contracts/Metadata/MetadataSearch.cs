using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Metadata;

public sealed class MetadataSearchRequest
{
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 25;
}

public sealed class MetadataSearchResponse
{
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("results")]
    public List<MetadataSearchResultDto> Results { get; init; } = [];
}

public sealed class MetadataSearchResultDto
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("year")]
    public string? Year { get; set; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; set; }

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

public sealed class FanOutSearchRequest
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; init; }

    [JsonPropertyName("max_results_per_provider")]
    public int MaxResultsPerProvider { get; init; } = 5;
}

public sealed class FanOutSearchResponse
{
    [JsonPropertyName("results")]
    public List<ProviderSearchResult> Results { get; init; } = [];

    [JsonPropertyName("total_providers")]
    public int TotalProviders { get; init; }

    [JsonPropertyName("responded_providers")]
    public int RespondedProviders { get; init; }

    [JsonPropertyName("elapsed_ms")]
    public double ElapsedMs { get; init; }
}

public sealed class ProviderSearchResult
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public List<FanOutSearchResultItem> Items { get; init; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

public sealed class FanOutSearchResultItem
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("author")]
    public string? Author { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("year")]
    public string? Year { get; init; }

    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("result_type")]
    public string? ResultType { get; init; }

    [JsonPropertyName("raw_fields")]
    public Dictionary<string, string> RawFields { get; init; } = [];
}

public sealed class CanonicalFieldDto
{
    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("provider_name")]
    public string? ProviderName { get; init; }

    [JsonPropertyName("is_user_locked")]
    public bool IsUserLocked { get; init; }

    [JsonPropertyName("is_conflicted")]
    public bool IsConflicted { get; init; }
}

public sealed class CoverFromUrlRequest
{
    [JsonPropertyName("image_url")]
    public string ImageUrl { get; init; } = string.Empty;
}

public sealed class SearchCacheUpsertRequest
{
    public string ResultsJson { get; init; } = "";
}
