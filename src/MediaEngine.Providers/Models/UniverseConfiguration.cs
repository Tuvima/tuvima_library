using System.Text.Json.Serialization;

namespace MediaEngine.Providers.Models;

/// <summary>
/// The complete knowledge model for a universe provider (currently: Wikidata).
///
/// <para>
/// Loaded from <c>config/universe/{provider}.json</c>. Contains the full property
/// map, bridge lookup priority, scope exclusions, and the Commons URL template.
/// </para>
///
/// <para>
/// The schema is intentionally generic — a different linked-data universe (not Wikidata)
/// could use the same model shape with different property codes and transforms.
/// </para>
/// </summary>
public sealed class UniverseConfiguration
{
    /// <summary>
    /// The universe provider name, e.g. <c>"wikidata"</c>.
    /// Must match the corresponding provider configuration name.
    /// </summary>
    [JsonPropertyName("provider_name")]
    public string ProviderName { get; set; } = "wikidata";

    /// <summary>
    /// The full property map. Key = P-code (e.g. <c>"P179"</c>),
    /// value = the property configuration describing how to interpret it.
    /// </summary>
    [JsonPropertyName("property_map")]
    public Dictionary<string, WikidataPropertyConfig> PropertyMap { get; set; } = [];

    /// <summary>
    /// Ordered list of bridge identifiers to try when resolving a QID from external IDs.
    /// The adapter tries each entry in order; first match wins.
    /// </summary>
    [JsonPropertyName("bridge_lookup_priority")]
    public List<BridgeLookupEntry> BridgeLookupPriority { get; set; } = [];

    /// <summary>
    /// P-codes to exclude from SPARQL queries for a given entity scope.
    /// Key = scope name (e.g. <c>"Work"</c>), value = list of P-codes to skip.
    /// Example: <c>{ "Work": ["P18"] }</c> — P18 (Image) is excluded from Work queries
    /// because media cover art comes from Apple Books/Audnexus/TMDB, not Wikidata.
    /// </summary>
    [JsonPropertyName("scope_exclusions")]
    public Dictionary<string, List<string>> ScopeExclusions { get; set; } = [];

    /// <summary>
    /// URL template for converting Wikimedia Commons filenames to thumbnail URLs.
    /// <c>{0}</c> is replaced with the URL-encoded filename.
    /// </summary>
    [JsonPropertyName("commons_url_template")]
    public string CommonsUrlTemplate { get; set; } =
        "https://commons.wikimedia.org/wiki/Special:FilePath/{0}?width=300";

    /// <summary>
    /// Maps media type names to lists of Wikidata Q-identifiers representing instance_of classes.
    /// Used by Tier 2 (Structured SPARQL Search) and Tier 3 (instance_of post-filtering)
    /// to constrain search results to the correct type of creative work.
    /// </summary>
    /// <example>
    /// <code>{ "Books": ["Q7725634", "Q571", "Q8261"], "Movies": ["Q11424"] }</code>
    /// </example>
    [JsonPropertyName("instance_of_classes")]
    public Dictionary<string, List<string>> InstanceOfClasses { get; set; } = [];

    /// <summary>
    /// Configurable thresholds and bonuses for Tier 2 structured SPARQL search scoring.
    /// All values have sensible compiled-in defaults; the config file can override any field.
    /// </summary>
    [JsonPropertyName("structured_search_thresholds")]
    public StructuredSearchThresholds SearchThresholds { get; set; } = new();
}

/// <summary>
/// Scoring thresholds and bonuses for Tier 2 structured SPARQL search.
/// All values are configurable via <c>config/universe/wikidata.json</c>.
/// </summary>
public sealed class StructuredSearchThresholds
{
    /// <summary>Auto-accept threshold when title + author + instance_of all match.</summary>
    [JsonPropertyName("auto_accept_with_author")]
    public double AutoAcceptWithAuthor { get; set; } = 0.85;

    /// <summary>Auto-accept threshold when title + year match.</summary>
    [JsonPropertyName("auto_accept_with_year")]
    public double AutoAcceptWithYear { get; set; } = 0.80;

    /// <summary>Auto-accept threshold when a single result matches title + instance_of.</summary>
    [JsonPropertyName("auto_accept_single_result")]
    public double AutoAcceptSingleResult { get; set; } = 0.70;

    /// <summary>Minimum score required to avoid review queue.</summary>
    [JsonPropertyName("review_threshold")]
    public double ReviewThreshold { get; set; } = 0.60;

    /// <summary>Bonus added when author/director name matches.</summary>
    [JsonPropertyName("author_match_bonus")]
    public double AuthorMatchBonus { get; set; } = 0.20;

    /// <summary>Bonus added when publication year matches.</summary>
    [JsonPropertyName("year_match_bonus")]
    public double YearMatchBonus { get; set; } = 0.10;

    /// <summary>Bonus added when the label is an exact (case-insensitive) match.</summary>
    [JsonPropertyName("exact_label_bonus")]
    public double ExactLabelBonus { get; set; } = 0.05;

    /// <summary>Bonus added when the query returns exactly one result.</summary>
    [JsonPropertyName("single_result_bonus")]
    public double SingleResultBonus { get; set; } = 0.05;

    /// <summary>
    /// Confidence boost applied when the QID was resolved via Tier 2 structured SPARQL.
    /// Analogous to the existing bridge boost (0.95) and general QID boost (0.92).
    /// </summary>
    [JsonPropertyName("tier2_title_boost")]
    public double Tier2TitleBoost { get; set; } = 0.88;

    /// <summary>Whether to enable instance_of post-filtering on Tier 3 (fuzzy title search) candidates.</summary>
    [JsonPropertyName("enable_tier3_instance_of_filtering")]
    public bool EnableTier3InstanceOfFiltering { get; set; } = true;
}
