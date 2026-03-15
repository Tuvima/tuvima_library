using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Configuration for the three-stage hydration pipeline.
///
/// Loaded from <c>config/hydration.json</c>. Controls concurrency, timeouts,
/// disambiguation thresholds, and the confidence gate that triggers review
/// queue entries.
///
/// <list type="bullet">
///   <item><b>Stage 1 — Authority Match:</b> Wikidata resolves the work's identity
///     via bridge IDs or title search, SPARQL deep hydration, Hub Intelligence,
///     and Person Enrichment.</item>
///   <item><b>Stage 2 — Context Match:</b> Wikipedia provides a human-readable
///     description using the QID from Stage 1.</item>
///   <item><b>Stage 3 — Retail Match:</b> runs retail providers in waterfall order
///     from <c>config/slots.json</c>, using bridge IDs from Stage 1 for precise
///     lookups.</item>
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

    /// <summary>Timeout in seconds for Stage 1 (Authority Match — Wikidata SPARQL).</summary>
    [JsonPropertyName("stage1_timeout_seconds")]
    public int Stage1TimeoutSeconds { get; set; } = 45;

    /// <summary>Timeout in seconds for Stage 2 (Context Match — Wikipedia REST API).</summary>
    [JsonPropertyName("stage2_timeout_seconds")]
    public int Stage2TimeoutSeconds { get; set; } = 15;

    /// <summary>Timeout in seconds for Stage 3 (Retail Match — provider waterfall).</summary>
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
    /// The service checks <c>IHydrationPipelineService.PendingCount</c> on
    /// this interval and only processes when the count is zero.
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
    /// Cron expression for the nightly sweep of stale Pass 2 items.
    /// Only the hour and minute fields are used (simple parsing).
    /// Default: <c>"0 2 * * *"</c> (2:00 AM daily).
    /// </summary>
    [JsonPropertyName("pass2_nightly_cron")]
    public string Pass2NightlyCron { get; set; } = "0 2 * * *";

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
