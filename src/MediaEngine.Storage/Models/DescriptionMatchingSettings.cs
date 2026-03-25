using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Configuration for description-based fuzzy matching during retail candidate scoring.
/// Each media category defines which file metadata fields should be matched against
/// retail provider description text, with configurable match types and weights.
/// Loaded from <c>config/description_matching.json</c>.
/// </summary>
public sealed class DescriptionMatchingSettings
{
    [JsonPropertyName("global_settings")]
    public DescriptionMatchGlobalSettings GlobalSettings { get; set; } = new();

    [JsonPropertyName("categories")]
    public Dictionary<string, DescriptionMatchCategory> Categories { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Global settings applied to all categories in description matching.
/// </summary>
public sealed class DescriptionMatchGlobalSettings
{
    /// <summary>Minimum FuzzySharp score (0-100) to count as a match.</summary>
    [JsonPropertyName("min_fuzzy_score")]
    public int MinFuzzyScore { get; set; } = 70;

    /// <summary>Maximum description length to search (truncate longer descriptions).</summary>
    [JsonPropertyName("description_max_chars")]
    public int DescriptionMaxChars { get; set; } = 2000;

    /// <summary>Whether matching is case-sensitive.</summary>
    [JsonPropertyName("case_sensitive")]
    public bool CaseSensitive { get; set; } = false;
}

/// <summary>
/// Category-level field list for description matching.
/// </summary>
public sealed class DescriptionMatchCategory
{
    [JsonPropertyName("fields")]
    public List<DescriptionMatchField> Fields { get; set; } = [];
}

/// <summary>
/// Configuration for a single field used in description-based candidate scoring.
/// </summary>
public sealed class DescriptionMatchField
{
    /// <summary>The key in the file hints dictionary to read the value from.</summary>
    [JsonPropertyName("file_hint_key")]
    public string FileHintKey { get; set; } = "";

    /// <summary>
    /// Match algorithm: "partial_ratio" (FuzzySharp substring), "token_set_ratio" (word reorder tolerant),
    /// "contains" (simple keyword check), "regex" (pattern extraction), "none" (disabled/reserved).
    /// </summary>
    [JsonPropertyName("match_type")]
    public string MatchType { get; set; } = "partial_ratio";

    /// <summary>
    /// Which candidate fields to match against. Defaults to ["description"] if not specified.
    /// Options: "description", "title", "copyright".
    /// </summary>
    [JsonPropertyName("match_against")]
    public List<string>? MatchAgainst { get; set; }

    /// <summary>For "contains" match type: keywords to check for.</summary>
    [JsonPropertyName("match_terms")]
    public List<string>? MatchTerms { get; set; }

    /// <summary>For "regex" match type: the regex pattern.</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>
    /// For "extract_then_compare" match type: regex patterns with a named capture group
    /// &lt;name&gt; to extract person names from the target text. The extracted name is then
    /// compared to the file hint value using FuzzySharp for precise name-to-name scoring.
    /// </summary>
    [JsonPropertyName("extraction_patterns")]
    public List<string>? ExtractionPatterns { get; set; }

    /// <summary>Weight of this field in the composite score (0.0-1.0).</summary>
    [JsonPropertyName("weight")]
    public double Weight { get; set; }

    /// <summary>Human-readable description of what this field matches.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
