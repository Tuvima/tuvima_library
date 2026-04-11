using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Configuration for the hydration pipeline.
///
/// Loaded from <c>config/hydration.json</c>. Controls concurrency, timeouts,
/// disambiguation thresholds, and the confidence gate that triggers review
/// queue entries.
///
/// <list type="bullet">
///   <item><b>Stage 1 — Retail Identification:</b> retail providers search catalogues
///     using file metadata. Cover art, descriptions, ratings, and bridge IDs
///     (ISBN, ASIN, TMDB ID) are gathered. <c>RetailMatchScoringService</c> gates
///     acceptance (≥0.85 auto, 0.50–0.85 review, &lt;0.50 discard).</item>
///   <item><b>Stage 2 — Wikidata Bridge Resolution:</b> uses bridge IDs from Stage 1
///     to resolve Wikidata QIDs for canonical identity, universe linkage,
///     and person enrichment. Requires Stage 1 success.</item>
///   <item><b>Stage 3 — Universe Enrichment:</b> background service for deep
///     relationship discovery, fictional entities, and image enrichment.
///     Runs on cron schedule.</item>
/// </list>
/// </summary>
public sealed class HydrationSettings
{
    /// <summary>
    /// Maximum concurrent provider calls within each stage.
    /// Shared across all providers in a given stage.
    /// </summary>
    [JsonPropertyName("stage_concurrency")]
    public int StageConcurrency { get; set; } = 3;

    /// <summary>Timeout in seconds for Stage 1 (Retail Identification — provider API calls).</summary>
    [JsonPropertyName("stage1_timeout_seconds")]
    public int Stage1TimeoutSeconds { get; set; } = 45;

    /// <summary>Timeout in seconds for Stage 2 (Wikidata Bridge Resolution).</summary>
    [JsonPropertyName("stage2_timeout_seconds")]
    public int Stage2TimeoutSeconds { get; set; } = 15;

    /// <summary>Timeout in seconds for Stage 3 (Universe Enrichment — background sweep).</summary>
    [JsonPropertyName("stage3_timeout_seconds")]
    public int Stage3TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum confidence required for a QID match to be accepted automatically
    /// during Stage 1 disambiguation. Below this threshold, multiple candidates
    /// are surfaced to the review queue.
    /// </summary>
    [JsonPropertyName("disambiguation_threshold")]
    public double DisambiguationThreshold { get; set; } = 0.7;

    /// <summary>
    /// After all stages complete, if the entity's overall confidence is below
    /// this threshold, a review queue entry is created for user verification.
    /// </summary>
    [JsonPropertyName("auto_review_confidence_threshold")]
    public double AutoReviewConfidenceThreshold { get; set; } = 0.60;

    /// <summary>
    /// Maximum number of QID candidates to store when disambiguation is needed.
    /// Prevents unbounded JSON payloads in the review queue.
    /// </summary>
    [JsonPropertyName("max_qid_candidates")]
    public int MaxQidCandidates { get; set; } = 5;

    /// <summary>
    /// When <c>true</c>, Stage 2 (Wikipedia) is skipped if Stage 1 did not
    /// resolve a Wikidata QID. Wikipedia requires a QID to look up the article
    /// via sitelinks, so skipping is the default behaviour.
    /// </summary>
    [JsonPropertyName("skip_wikipedia_without_qid")]
    public bool SkipWikipediaWithoutQid { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, the pipeline continues to Stage 2 + Stage 3 even if
    /// Stage 1 (Wikidata) failed to resolve a QID. The retail providers in
    /// Stage 3 fall back to title-based search without bridge IDs.
    /// </summary>
    [JsonPropertyName("continue_pipeline_on_authority_failure")]
    public bool ContinuePipelineOnAuthorityFailure { get; set; } = true;

    /// <summary>
    /// Maximum character length for Wikipedia description extracts.
    /// Longer extracts are truncated with an ellipsis.
    /// </summary>
    [JsonPropertyName("wikipedia_description_max_chars")]
    public int WikipediaDescriptionMaxChars { get; set; } = 1000;

    /// <summary>
    /// Minimum confidence for a Wikidata title search match to be auto-accepted
    /// during Stage 1 (Authority Match). Below this threshold, a
    /// <see cref="Domain.Enums.ReviewTrigger.AuthorityMatchFailed"/> review item
    /// is created for user verification.
    /// </summary>
    [JsonPropertyName("universe_title_search_auto_accept")]
    public double UniverseTitleSearchAutoAccept { get; set; } = 0.80;

    /// <summary>
    /// Debounce interval in seconds for universe.xml writes after fictional
    /// entity enrichment. During a burst of enrichments for the same universe,
    /// the writer waits this long after the last enrichment before writing.
    /// </summary>
    [JsonPropertyName("universe_xml_write_debounce_seconds")]
    public int UniverseXmlWriteDebounceSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum depth for recursive fictional entity relationship population.
    /// Target entities are created but NOT enriched beyond this depth.
    /// </summary>
    [JsonPropertyName("fictional_entity_enrichment_depth")]
    public int FictionalEntityEnrichmentDepth { get; set; } = 2;

    /// <summary>
    /// Confidence threshold for Stage 3 retail waterfall. After each provider
    /// in the waterfall produces claims, overall confidence is checked. If it
    /// reaches this threshold, the waterfall stops. Set to 1.0 to always run
    /// all providers; set to 0.0 to stop after the primary.
    /// </summary>
    [JsonPropertyName("stage3_waterfall_confidence_threshold")]
    public double Stage3WaterfallConfidenceThreshold { get; set; } = 0.65;

    /// <summary>
    /// When a Wikidata QID is confirmed during hydration, the auto-organize
    /// confidence gate is lowered from <see cref="MediaEngine.Intelligence.ScoringConfiguration.AutoLinkThreshold"/>
    /// (0.85) to this value.  This allows audiobooks and other media types
    /// with conservative processor confidence to be organized once their
    /// identity is positively confirmed.
    /// </summary>
    [JsonPropertyName("post_hydration_organize_threshold")]
    public double PostHydrationOrganizeThreshold { get; set; } = 0.70;

    /// <summary>
    /// Minimum number of distinct works required for a universe folder to be
    /// created.  Standalone works (e.g. The Martian) with fewer than this many
    /// linked works will not generate a <c>.universe/</c> folder.
    /// </summary>
    [JsonPropertyName("minimum_universe_work_count")]
    public int MinimumUniverseWorkCount { get; set; } = 2;

    // ── Two-Pass Enrichment Architecture ──────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the hydration pipeline splits into two passes:
    /// Pass 1 (Quick Match) fetches core identity + cover art immediately,
    /// and Pass 2 (Universe Lookup) runs later for deep enrichment.
    /// When <c>false</c>, all requests run the full pipeline (backward compat).
    /// </summary>
    [JsonPropertyName("two_pass_enabled")]
    public bool TwoPassEnabled { get; set; } = true;

    /// <summary>
    /// When <c>true</c> and two-pass is enabled, Pass 1 uses a reduced SPARQL
    /// query fetching only core properties (title, author, year, genre, series,
    /// series_position) plus bridge IDs. When <c>false</c>, Pass 1 uses the
    /// full 50+ property query.
    /// </summary>
    [JsonPropertyName("pass1_core_properties_only")]
    public bool Pass1CorePropertiesOnly { get; set; } = true;

    /// <summary>
    /// Seconds to wait between idle checks before processing Pass 2 items.
    /// The service queries <c>IIdentityJobRepository.CountActiveAsync</c> on
    /// this interval and only processes when the active job count is zero.
    /// </summary>
    [JsonPropertyName("pass2_idle_delay_seconds")]
    public int Pass2IdleDelaySeconds { get; set; } = 10;

    /// <summary>
    /// Milliseconds to delay between each Pass 2 item to respect rate limits.
    /// Prevents overwhelming external APIs during bulk enrichment.
    /// </summary>
    [JsonPropertyName("pass2_rate_limit_ms")]
    public int Pass2RateLimitMs { get; set; } = 2000;

    /// <summary>
    /// Hours after which a pending Pass 2 item is considered stale.
    /// The nightly sweep prioritises items older than this threshold.
    /// </summary>
    [JsonPropertyName("pass2_stale_threshold_hours")]
    public int Pass2StaleThresholdHours { get; set; } = 24;

    /// <summary>
    /// Maximum number of Pass 2 items to process in a single batch.
    /// Limits processing duration per cycle.
    /// </summary>
    [JsonPropertyName("pass2_batch_size")]
    public int Pass2BatchSize { get; set; } = 50;

    // ── Batch Reconciliation ──────────────────────────────────────────

    /// <summary>
    /// Maximum time in milliseconds to wait for additional requests before
    /// flushing a batch to the Reconciliation API. When multiple files arrive
    /// close together (bulk import), the pipeline accumulates them into a single
    /// batch API call instead of making one call per file.
    /// </summary>
    [JsonPropertyName("batch_accumulation_timeout_ms")]
    public int BatchAccumulationTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Minimum number of requests required to trigger batch reconciliation.
    /// If fewer requests accumulate before the timeout, they are processed
    /// individually (degenerate batch of 1). Set to 1 to always batch.
    /// </summary>
    [JsonPropertyName("batch_min_size")]
    public int BatchMinSize { get; set; } = 2;

    /// <summary>
    /// Maximum batch size for Reconciliation API calls. The Wikidata
    /// Reconciliation API supports up to 50 queries per POST.
    /// </summary>
    [JsonPropertyName("batch_max_size")]
    public int BatchMaxSize { get; set; } = 50;

    // ── Retail-First Pipeline ──────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, Stage 0 (Local Match) checks the bridge_ids table and
    /// canonical_values for existing matches before making any external API calls.
    /// Dramatically reduces external calls for episodic content (TV, music).
    /// </summary>
    [JsonPropertyName("local_match_enabled")]
    public bool LocalMatchEnabled { get; set; } = true;

    /// <summary>
    /// Minimum fuzzy match confidence for a title+author match against existing
    /// canonical values. Very high threshold (0.95) to prevent false positives.
    /// Only used when no exact ID match is found.
    /// </summary>
    [JsonPropertyName("local_match_fuzzy_threshold")]
    public double LocalMatchFuzzyThreshold { get; set; } = 0.95;

    /// <summary>
    /// Minimum composite confidence for a retail match to be auto-accepted
    /// during Stage 1 (Retail Identification). Below this threshold, the
    /// match goes to review queue as <see cref="Domain.Enums.ReviewTrigger.RetailMatchAmbiguous"/>.
    /// </summary>
    [JsonPropertyName("retail_auto_accept_threshold")]
    public double RetailAutoAcceptThreshold { get; set; } = 0.85;

    /// <summary>
    /// Below this threshold, a retail match is treated as too weak to accept
    /// even provisionally. The item is flagged as <see cref="Domain.Enums.ReviewTrigger.RetailMatchFailed"/>.
    /// Between this and <see cref="RetailAutoAcceptThreshold"/>, the match is
    /// provisionally accepted and sent to review.
    /// </summary>
    [JsonPropertyName("retail_ambiguous_threshold")]
    public double RetailAmbiguousThreshold { get; set; } = 0.50;

    /// <summary>
    /// Media types for which the pipeline resolves Wikidata edition entities
    /// (via P629 edition_or_translation_of) in addition to work entities.
    /// Media types NOT in this list resolve directly to work QIDs.
    /// Configurable to allow adding/removing edition awareness without code changes.
    /// </summary>
    [JsonPropertyName("edition_aware_media_types")]
    public List<string> EditionAwareMediaTypes { get; set; } =
        ["Books", "Audiobooks", "Movies", "Comics", "Music"];

    /// <summary>
    /// Weights for fuzzy field matching during Stage 1 retail scoring.
    /// Keys: "title", "author", "year", "format". Values: 0.0–1.0.
    /// Must sum to 1.0. Read by <c>RetailMatchScoringService</c>.
    /// </summary>
    [JsonPropertyName("fuzzy_match_weights")]
    public Dictionary<string, double> FuzzyMatchWeights { get; set; } = new()
    {
        ["title"] = 0.45,
        ["author"] = 0.35,
        ["year"] = 0.10,
        ["format"] = 0.10,
    };

    /// <summary>
    /// HTML tags to preserve when sanitizing retail provider descriptions.
    /// All other tags are stripped. Used by the <c>sanitize_html</c> transform.
    /// </summary>
    [JsonPropertyName("preserve_html_tags")]
    public List<string> PreserveHtmlTags { get; set; } =
        ["b", "i", "em", "strong", "p", "br"];

    /// <summary>
    /// Maximum entities per Wikidata batch API call during Stage 2
    /// (Wikidata Bridge Resolution). The Data Extension API supports up to 50.
    /// </summary>
    [JsonPropertyName("wikidata_batch_size")]
    public int WikidataBatchSize { get; set; } = 50;


    // ── Chronicle Engine ──────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the qualified statement SPARQL syntax (p:/ps:/pq:)
    /// is used for relationship properties to capture P580 (start time) and
    /// P582 (end time) temporal qualifiers.
    /// </summary>
    [JsonPropertyName("fetch_temporal_qualifiers")]
    public bool FetchTemporalQualifiers { get; set; } = true;

    /// <summary>Maximum entities per batch SPARQL query using VALUES clause.</summary>
    [JsonPropertyName("batch_sparql_size")]
    public int BatchSparqlSize { get; set; } = 50;

    /// <summary>Maximum depth for lineage traversal (2 = grandparents/grandchildren).</summary>
    [JsonPropertyName("lineage_depth")]
    public int LineageDepth { get; set; } = 2;

    /// <summary>When <c>true</c>, the Lore Delta check runs on Chronicle Explorer page load.</summary>
    [JsonPropertyName("lore_delta_check_on_explorer_open")]
    public bool LoreDeltaCheckOnExplorerOpen { get; set; } = true;

    /// <summary>When <c>true</c>, Canon Discrepancy detection runs during Stage 1 hydration.</summary>
    [JsonPropertyName("canon_discrepancy_detection")]
    public bool CanonDiscrepancyDetection { get; set; } = true;

    /// <summary>When <c>true</c>, era-correct actor resolution uses temporal qualifiers on performer edges.</summary>
    [JsonPropertyName("era_actor_resolution")]
    public bool EraActorResolution { get; set; } = true;

    // ── Stage 3: Universe Enrichment (Background Service) ──────────────

    /// <summary>
    /// When <c>true</c>, the Universe Enrichment background service runs on
    /// its cron schedule. When <c>false</c>, Stage 3 is skipped entirely.
    /// </summary>
    [JsonPropertyName("stage3_enabled")]
    public bool Stage3Enabled { get; set; } = true;

    /// <summary>
    /// Milliseconds to delay between each work during a Stage 3 sweep.
    /// Prevents overwhelming external APIs (Wikidata, Fanart.tv).
    /// </summary>
    [JsonPropertyName("stage3_rate_limit_ms")]
    public int Stage3RateLimitMs { get; set; } = 3000;

    /// <summary>
    /// Maximum number of works to process in a single Stage 3 sweep.
    /// Limits processing duration per cycle.
    /// </summary>
    [JsonPropertyName("stage3_max_items_per_sweep")]
    public int Stage3MaxItemsPerSweep { get; set; } = 50;

    /// <summary>
    /// Number of days after which a previously enriched work is considered stale
    /// and eligible for re-enrichment during a Stage 3 sweep.
    /// </summary>
    [JsonPropertyName("stage3_refresh_days")]
    public int Stage3RefreshDays { get; set; } = 30;

    /// <summary>
    /// Maximum depth for recursive fictional entity discovery during Stage 3.
    /// Overrides <see cref="FictionalEntityEnrichmentDepth"/> within the
    /// background service context.
    /// </summary>
    [JsonPropertyName("stage3_max_depth")]
    public int Stage3MaxDepth { get; set; } = 2;

    /// <summary>
    /// Number of days to retain entity timeline events before automatic culling.
    /// The most recent event per entity per stage is always preserved regardless
    /// of age. Set to 0 to disable culling. Default: 365 days.
    /// </summary>
    [JsonPropertyName("timeline_retention_days")]
    public int TimelineRetentionDays { get; set; } = 365;

    // ── Backward compatibility ──────────────────────────────────────────

    /// <summary>
    /// Legacy alias for <see cref="Stage3WaterfallConfidenceThreshold"/>.
    /// Existing <c>hydration.json</c> files may use the old key name.
    /// </summary>
    [JsonPropertyName("stage1_waterfall_confidence_threshold")]
    [System.Obsolete("Use stage3_waterfall_confidence_threshold")]
    public double? LegacyStage1WaterfallConfidenceThreshold
    {
        get => null;
        set { if (value.HasValue) Stage3WaterfallConfidenceThreshold = value.Value; }
    }

    /// <summary>
    /// Legacy alias for <see cref="SkipWikipediaWithoutQid"/>.
    /// Existing <c>hydration.json</c> files may use the old key name.
    /// </summary>
    [JsonPropertyName("skip_stage2_without_bridge_ids")]
    [System.Obsolete("Use skip_wikipedia_without_qid")]
    public bool? LegacySkipStage2WithoutBridgeIds
    {
        get => null;
        set { if (value.HasValue) SkipWikipediaWithoutQid = value.Value; }
    }
}
