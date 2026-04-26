using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>
/// Request body for <c>POST /metadata/search-all</c>.
/// Searches all eligible providers concurrently and returns merged results.
/// </summary>
public sealed class FanOutSearchRequest
{
    /// <summary>The search query (usually a title or title + author).</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Media type to scope the search. Null or empty = all providers.
    /// </summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>
    /// Optional: limit to a single provider. Null = fan-out to all.
    /// </summary>
    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; init; }

    /// <summary>Maximum results per provider. Default: 5.</summary>
    [JsonPropertyName("max_results_per_provider")]
    public int MaxResultsPerProvider { get; init; } = 5;
}

/// <summary>
/// Response body for <c>POST /metadata/search-all</c>.
/// Contains merged results from multiple providers.
/// </summary>
public sealed class FanOutSearchResponse
{
    /// <summary>Results grouped by provider.</summary>
    [JsonPropertyName("results")]
    public List<ProviderSearchResult> Results { get; init; } = [];

    /// <summary>Total number of providers queried.</summary>
    [JsonPropertyName("total_providers")]
    public int TotalProviders { get; init; }

    /// <summary>Number of providers that responded successfully.</summary>
    [JsonPropertyName("responded_providers")]
    public int RespondedProviders { get; init; }

    /// <summary>Total elapsed time in milliseconds.</summary>
    [JsonPropertyName("elapsed_ms")]
    public double ElapsedMs { get; init; }
}

/// <summary>
/// Results from a single provider in a fan-out search.
/// </summary>
public sealed class ProviderSearchResult
{
    /// <summary>Provider GUID.</summary>
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    /// <summary>Provider display name.</summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>Individual search result items.</summary>
    [JsonPropertyName("items")]
    public List<FanOutSearchResultItem> Items { get; init; } = [];

    /// <summary>Error message if the provider failed. Null = success.</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// A single search result item in a fan-out search response.
/// Includes all raw extracted fields for the diff grid.
/// </summary>
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

    /// <summary>
    /// All raw fields extracted by the provider's field mappings.
    /// Keys are claim keys (e.g. "isbn", "series", "narrator").
    /// Used by the edit panel diff grid.
    /// </summary>
    [JsonPropertyName("raw_fields")]
    public Dictionary<string, string> RawFields { get; init; } = [];
}

/// <summary>
/// A canonical field with its current value, confidence, and provenance.
/// Returned by <c>GET /metadata/canonical/{entityId}</c>.
/// </summary>
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

/// <summary>
/// Request body for <c>POST /metadata/{entityId}/cover-from-url</c>.
/// </summary>
public sealed class CoverFromUrlRequest
{
    [JsonPropertyName("image_url")]
    public string ImageUrl { get; init; } = string.Empty;
}

/// <summary>Request for batch libraryItem operations.</summary>
public sealed class BatchLibraryItemRequest
{
    [JsonPropertyName("entity_ids")]
    public Guid[] EntityIds { get; init; } = [];
}

/// <summary>Response from batch libraryItem operations.</summary>
public sealed class BatchLibraryItemResponse
{
    [JsonPropertyName("processed_count")]
    public int ProcessedCount { get; init; }

    [JsonPropertyName("total_requested")]
    public int TotalRequested { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

/// <summary>Request body for caching search results.</summary>
public sealed class SearchCacheUpsertRequest
{
    public string ResultsJson { get; init; } = "";
}
