using System.Text.Json.Serialization;

namespace MediaEngine.Api.Models;

/// <summary>
/// Request body for <c>POST /metadata/search</c>.
/// Searches an external metadata provider and returns multiple result candidates
/// for the user to choose from.
/// </summary>
public sealed class MetadataSearchRequest
{
    /// <summary>
    /// The registered name of the provider to search (e.g. <c>"apple_books_ebook"</c>).
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>The search query (usually a title or title + author).</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>
    /// Media type to scope the search. Values: <c>"Epub"</c>, <c>"Audiobook"</c>,
    /// <c>"Movies"</c>, <c>"Comic"</c>, <c>"TV"</c>, <c>"Music"</c>.
    /// </summary>
    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    /// <summary>Maximum number of results to return. Default: 25.</summary>
    [JsonPropertyName("limit")]
    public int Limit { get; init; } = 25;
}

/// <summary>
/// Response body for <c>POST /metadata/search</c>.
/// </summary>
public sealed class MetadataSearchResponse
{
    /// <summary>The provider that was searched.</summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>The search query used.</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    /// <summary>List of matching results.</summary>
    [JsonPropertyName("results")]
    public List<SearchResultResponse> Results { get; init; } = [];
}

/// <summary>
/// A single search result item in the metadata search response.
/// </summary>
public sealed class SearchResultResponse
{
    /// <summary>Title of the matched item.</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>Author, artist, or creator name.</summary>
    [JsonPropertyName("author")]
    public string? Author { get; init; }

    /// <summary>Short description or summary.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Publication or release year.</summary>
    [JsonPropertyName("year")]
    public string? Year { get; init; }

    /// <summary>URL to a thumbnail/cover image.</summary>
    [JsonPropertyName("thumbnail_url")]
    public string? ThumbnailUrl { get; init; }

    /// <summary>Provider-specific item ID for subsequent direct lookup.</summary>
    [JsonPropertyName("provider_item_id")]
    public string? ProviderItemId { get; init; }

    /// <summary>Match confidence score (0.0–1.0).</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}
