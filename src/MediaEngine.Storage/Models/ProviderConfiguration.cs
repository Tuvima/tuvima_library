using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Per-provider configuration loaded from <c>config/providers/{name}.json</c>.
///
/// Each metadata provider carries its own self-contained configuration including
/// scoring weight, endpoints, field weights, rate limits, and capability tags.
/// The provider configuration controls how the scoring engine and harvesting
/// pipeline treat this provider.
/// </summary>
public sealed class ProviderConfiguration
{
    /// <summary>Must match the provider's registered name in the provider registry.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Semantic version of the provider adapter.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>When <c>false</c>, the provider is skipped during harvesting.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Default scoring weight for this provider across all metadata fields.
    /// Individual fields can be overridden via <see cref="FieldWeights"/>.
    /// </summary>
    [JsonPropertyName("weight")]
    public double Weight { get; set; } = 1.0;

    /// <summary>
    /// The media domain this provider specialises in.
    /// Used for Dashboard UI grouping and future domain-filtered scoring.
    /// </summary>
    [JsonPropertyName("domain")]
    public ProviderDomain Domain { get; set; } = ProviderDomain.Universal;

    /// <summary>
    /// Relative path to the provider's SVG icon (e.g. <c>"images/providers/apple_books.svg"</c>).
    /// Used by the Dashboard to display a visual identifier for each provider.
    /// The path is relative to <c>wwwroot/</c>.
    /// </summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>
    /// Declarative list of metadata fields this provider is considered an expert in
    /// (e.g. <c>["cover", "narrator", "series"]</c>). Shown in the Dashboard.
    /// </summary>
    [JsonPropertyName("capability_tags")]
    public List<string> CapabilityTags { get; set; } = [];

    /// <summary>
    /// Complete list of canonical claim keys this provider can supply.
    /// Used by the Dashboard to show all fields available from each provider
    /// and allow users to toggle which fields are active.
    /// Keys reference entries in <c>config/field_normalization.json</c>.
    /// </summary>
    [JsonPropertyName("available_fields")]
    public List<string> AvailableFields { get; set; } = [];

    /// <summary>
    /// Per-field weight overrides. Key = claim key (e.g. <c>"cover"</c>),
    /// value = weight in [0.0, 1.0]. Falls back to <see cref="Weight"/> if absent.
    /// </summary>
    [JsonPropertyName("field_weights")]
    public Dictionary<string, double> FieldWeights { get; set; } = [];

    /// <summary>
    /// Named endpoint URLs for this provider. Convention:
    /// <c>"api"</c> for the primary endpoint; additional keys for secondary
    /// endpoints (e.g. <c>"sparql"</c> for Wikidata).
    /// </summary>
    [JsonPropertyName("endpoints")]
    public Dictionary<string, string> Endpoints { get; set; } = [];

    /// <summary>
    /// Minimum milliseconds between consecutive calls to this provider.
    /// <c>0</c> = no throttle. Examples: Apple Books 300, Wikidata 1100, Audnexus 0.
    /// </summary>
    [JsonPropertyName("throttle_ms")]
    public int ThrottleMs { get; set; }

    /// <summary>
    /// Maximum concurrent requests to this provider.
    /// Defaults to <c>1</c> (serial). Increase for providers that allow parallel calls.
    /// </summary>
    [JsonPropertyName("max_concurrency")]
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>
    /// Which stages of the three-stage hydration pipeline this provider participates in.
    /// Values: <c>1</c> (Retail Match), <c>2</c> (Universal Bridge), <c>3</c> (Human Hub).
    ///
    /// A provider can declare multiple stages (e.g. Audnexus declares <c>[1, 3]</c>
    /// for both retail matching and narrator enrichment).
    /// Defaults to <c>[1]</c> (Retail Match only).
    /// </summary>
    [JsonPropertyName("hydration_stages")]
    public List<int> HydrationStages { get; set; } = [1];

    /// <summary>
    /// Declares which bridge identifiers this provider works best with per media type.
    /// Key = media type display name (e.g. <c>"Books"</c>, <c>"Audiobooks"</c>),
    /// value = ordered list of canonical claim keys (e.g. <c>["isbn", "apple_books_id"]</c>).
    ///
    /// Used by <c>ExtractBridgeHints</c> to determine which canonical values should
    /// flow from Stage 1 to Stage 2 as bridge hints for precise retail lookups.
    /// </summary>
    [JsonPropertyName("preferred_bridge_ids")]
    public Dictionary<string, List<string>>? PreferredBridgeIds { get; set; }

    /// <summary>
    /// Time-to-live in hours for cached HTTP responses from this provider.
    /// When set, the <c>ConfigDrivenAdapter</c> caches raw JSON responses and
    /// skips HTTP calls for repeated queries within the TTL window.
    /// Default: <c>168</c> (7 days). Set <c>null</c> or <c>0</c> to disable caching.
    /// Comic Vine recommends a long TTL (720 hours / 30 days) due to strict rate limits.
    /// </summary>
    [JsonPropertyName("cache_ttl_hours")]
    public int? CacheTtlHours { get; set; }

    // ── Config-driven adapter fields ─────────────────────────────────────────

    /// <summary>
    /// Adapter type: <c>"config_driven"</c> uses the universal adapter that reads
    /// behaviour from this config file. <c>"coded"</c> (default) uses a hard-coded
    /// adapter class (e.g. Wikidata's SPARQL adapter).
    /// </summary>
    [JsonPropertyName("adapter_type")]
    public string AdapterType { get; set; } = "coded";

    /// <summary>
    /// Stable GUID identifying this provider across all <c>metadata_claims.provider_id</c> rows.
    /// Required for <c>config_driven</c> adapters; ignored for <c>coded</c> adapters.
    /// </summary>
    [JsonPropertyName("provider_id")]
    public string? ProviderId { get; set; }

    /// <summary>Human-readable provider name for the Dashboard.</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    /// <summary>HTTP client configuration for config-driven adapters.</summary>
    [JsonPropertyName("http_client")]
    public HttpClientConfig? HttpClient { get; set; }

    /// <summary>Media type and entity type filters for config-driven adapters.</summary>
    [JsonPropertyName("can_handle")]
    public CanHandleConfig? CanHandle { get; set; }

    /// <summary>
    /// Ordered list of search strategies. Tried in <see cref="SearchStrategyConfig.Priority"/>
    /// order; first strategy that returns results wins.
    /// </summary>
    [JsonPropertyName("search_strategies")]
    public List<SearchStrategyConfig>? SearchStrategies { get; set; }

    /// <summary>
    /// Field extraction mappings. Each entry maps a JSON path in the API response
    /// to a claim key with a confidence value and optional transform.
    /// </summary>
    [JsonPropertyName("field_mappings")]
    public List<FieldMappingConfig>? FieldMappings { get; set; }

    /// <summary>
    /// When <c>true</c>, this provider requires an API key to function.
    /// The Dashboard shows this as a configuration requirement.
    /// Zero-key providers (the default) work without any authentication.
    /// </summary>
    [JsonPropertyName("requires_api_key")]
    public bool RequiresApiKey { get; set; }

    /// <summary>
    /// Optional Material icon name for the Dashboard UI (e.g. "MenuBook", "Headphones").
    /// When set, overrides the default accent icon from <c>ProviderAccentMap</c>.
    /// </summary>
    [JsonPropertyName("custom_icon_name")]
    public string? CustomIconName { get; set; }
}

/// <summary>HTTP client configuration for config-driven provider adapters.</summary>
public sealed class HttpClientConfig
{
    [JsonPropertyName("timeout_seconds")]
    public int TimeoutSeconds { get; set; } = 10;

    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; set; }

    /// <summary>
    /// API key or token for authenticated providers (e.g. TMDB Bearer token, Comic Vine API key).
    /// Stored in the provider config file. Empty or <c>null</c> = no key configured.
    /// </summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// How the API key is delivered to the provider. Values:
    /// <c>"bearer"</c> — sent as <c>Authorization: Bearer {key}</c> header (e.g. TMDB).
    /// <c>"query"</c> — appended as a query parameter using <see cref="ApiKeyParamName"/> (e.g. Comic Vine).
    /// <c>"header"</c> — sent as a custom header using <see cref="ApiKeyParamName"/> as the header name.
    /// Default: <c>null</c> (no API key delivery).
    /// </summary>
    [JsonPropertyName("api_key_delivery")]
    public string? ApiKeyDelivery { get; set; }

    /// <summary>
    /// The query parameter name (for <c>"query"</c> delivery) or header name (for <c>"header"</c> delivery).
    /// Example: <c>"api_key"</c> for Comic Vine query parameter.
    /// </summary>
    [JsonPropertyName("api_key_param_name")]
    public string? ApiKeyParamName { get; set; }
}

/// <summary>Media type and entity type capability filters for config-driven adapters.</summary>
public sealed class CanHandleConfig
{
    [JsonPropertyName("media_types")]
    public List<string> MediaTypes { get; set; } = [];

    [JsonPropertyName("entity_types")]
    public List<string> EntityTypes { get; set; } = [];
}

/// <summary>
/// A single search strategy for a config-driven adapter.
/// Strategies are tried in <see cref="Priority"/> order; first success wins.
/// </summary>
public sealed class SearchStrategyConfig
{
    /// <summary>Human-readable strategy name for logging.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Execution priority — lower numbers tried first.</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; }

    /// <summary>
    /// Request fields that must be non-empty for this strategy to attempt.
    /// Values: <c>"title"</c>, <c>"author"</c>, <c>"isbn"</c>, <c>"asin"</c>, etc.
    /// </summary>
    [JsonPropertyName("required_fields")]
    public List<string> RequiredFields { get; set; } = [];

    /// <summary>
    /// URL template with placeholders: <c>{base_url}</c>, <c>{title}</c>, <c>{author}</c>,
    /// <c>{isbn}</c>, <c>{asin}</c>, <c>{query}</c>. All values are URI-escaped.
    /// </summary>
    [JsonPropertyName("url_template")]
    public string UrlTemplate { get; set; } = string.Empty;

    /// <summary>
    /// Template for building the <c>{query}</c> placeholder.
    /// Example: <c>"{title} {author}"</c>. Missing fields are omitted.
    /// </summary>
    [JsonPropertyName("query_template")]
    public string? QueryTemplate { get; set; }

    /// <summary>
    /// JSON path to the results array in the response (e.g. <c>"docs"</c>, <c>"items"</c>).
    /// When absent, the response is treated as a direct result object.
    /// </summary>
    [JsonPropertyName("results_path")]
    public string? ResultsPath { get; set; }

    /// <summary>Index within the results array to use. Default: 0 (first result).</summary>
    [JsonPropertyName("result_index")]
    public int ResultIndex { get; set; }

    /// <summary>
    /// When <c>true</c>, HTTP 404 is treated as "no results" rather than an error.
    /// Useful for direct-lookup APIs like Audnexus.
    /// </summary>
    [JsonPropertyName("tolerate_404")]
    public bool Tolerate404 { get; set; }

    /// <summary>
    /// Maximum number of results to return in multi-result search mode.
    /// Overrides the <c>limit</c> parameter passed to <c>SearchAsync</c> when set
    /// to a positive value. Default 0 means "use the caller's limit".
    /// </summary>
    [JsonPropertyName("max_results")]
    public int MaxResults { get; set; }

    /// <summary>
    /// Number of results to request from the API during automatic single-result matching
    /// (<c>FetchAsync</c>). Because only <c>result_index</c> + 1 results are used,
    /// there is no need to request the full <c>max_results</c> count.
    /// Replaces the <c>{limit}</c> placeholder in the URL template when called via
    /// <c>FetchAsync</c>. Default: 5.
    /// </summary>
    [JsonPropertyName("fetch_limit")]
    public int FetchLimit { get; set; } = 5;

    /// <summary>
    /// Optional media type filter. When non-empty, this strategy is only used
    /// when the request's media type matches one of these values.
    /// When empty/null, the strategy is used for all media types.
    /// Values: <c>"Books"</c>, <c>"Audiobooks"</c>, <c>"Movies"</c>, <c>"Comics"</c>, <c>"TV"</c>.
    /// </summary>
    [JsonPropertyName("media_types")]
    public List<string>? MediaTypes { get; set; }
}

/// <summary>
/// Maps a JSON path in the API response to a metadata claim.
/// </summary>
public sealed class FieldMappingConfig
{
    /// <summary>The metadata claim key (e.g. <c>"title"</c>, <c>"cover"</c>).</summary>
    [JsonPropertyName("claim_key")]
    public string ClaimKey { get; set; } = string.Empty;

    /// <summary>
    /// JSON path expression to extract the value. Supports dot-notation,
    /// array indexing (<c>[0]</c>), and wildcard iteration (<c>[*].name</c>).
    /// </summary>
    [JsonPropertyName("json_path")]
    public string JsonPath { get; set; } = string.Empty;

    /// <summary>Confidence value (0.0–1.0) assigned to claims produced by this mapping.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; } = 0.5;

    /// <summary>
    /// Optional named transform to apply to the extracted value.
    /// See <c>ValueTransformRegistry</c> for available transforms.
    /// </summary>
    [JsonPropertyName("transform")]
    public string? Transform { get; set; }

    /// <summary>
    /// Optional arguments passed to the transform function.
    /// Interpretation is transform-specific.
    /// </summary>
    [JsonPropertyName("transform_args")]
    public string? TransformArgs { get; set; }

    /// <summary>
    /// Optional media type filter. When non-empty, this mapping is only used
    /// when the request's media type matches one of these values.
    /// When empty/null, the mapping is used for all media types.
    /// Values: <c>"Books"</c>, <c>"Audiobooks"</c>, <c>"Movies"</c>, <c>"Comics"</c>, <c>"TV"</c>.
    /// </summary>
    [JsonPropertyName("media_types")]
    public List<string>? MediaTypes { get; set; }
}
