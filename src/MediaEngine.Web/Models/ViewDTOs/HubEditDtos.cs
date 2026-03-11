using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

/// <summary>
/// A canonical field with its current value, confidence, and provenance.
/// </summary>
public sealed record CanonicalFieldViewModel(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("provider_name")] string? ProviderName,
    [property: JsonPropertyName("is_user_locked")] bool IsUserLocked,
    [property: JsonPropertyName("is_conflicted")] bool IsConflicted)
{
    /// <summary>Human-readable display name for the field.</summary>
    public string DisplayName => Key switch
    {
        "title" => "Title",
        "author" => "Author",
        "year" => "Year",
        "description" => "Description",
        "cover" => "Cover Art",
        "isbn" => "ISBN",
        "asin" => "ASIN",
        "narrator" => "Narrator",
        "series" => "Series",
        "series_position" => "Series Position",
        "genre" => "Genre",
        "publisher" => "Publisher",
        "page_count" => "Page Count",
        "language" => "Language",
        "media_type" => "Media Type",
        "dominant_color" => "Dominant Color",
        "hero" => "Hero Banner",
        "wikidata_qid" => "Wikidata QID",
        _ => Key.Replace("_", " ").ToUpperInvariant(),
    };
}

/// <summary>
/// Response from the fan-out metadata search.
/// </summary>
public sealed class FanOutSearchResponseViewModel
{
    [JsonPropertyName("results")]
    public List<ProviderSearchResultViewModel> Results { get; init; } = [];

    [JsonPropertyName("total_providers")]
    public int TotalProviders { get; init; }

    [JsonPropertyName("responded_providers")]
    public int RespondedProviders { get; init; }

    [JsonPropertyName("elapsed_ms")]
    public double ElapsedMs { get; init; }
}

/// <summary>
/// Results from a single provider in a fan-out search.
/// </summary>
public sealed class ProviderSearchResultViewModel
{
    [JsonPropertyName("provider_id")]
    public string ProviderId { get; init; } = string.Empty;

    [JsonPropertyName("provider_name")]
    public string ProviderName { get; init; } = string.Empty;

    [JsonPropertyName("items")]
    public List<SearchResultItemViewModel> Items { get; init; } = [];

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}

/// <summary>
/// A single search result item from a fan-out search.
/// </summary>
public sealed class SearchResultItemViewModel
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

    [JsonPropertyName("raw_fields")]
    public Dictionary<string, string> RawFields { get; init; } = [];
}

/// <summary>
/// A single entry in the field diff grid. Compares current canonical value
/// against a provider's search result value.
/// </summary>
public sealed record FieldDiffEntry(
    string Key,
    string DisplayName,
    string? CurrentValue,
    double? CurrentConfidence,
    string? ProviderValue,
    bool IsDifferent,
    bool IsSelected);
