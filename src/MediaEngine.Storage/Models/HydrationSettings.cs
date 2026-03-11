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
    /// Confidence threshold for Stage 3 retail waterfall. After each provider
    /// in the waterfall produces claims, overall confidence is checked. If it
    /// reaches this threshold, the waterfall stops. Set to 1.0 to always run
    /// all providers; set to 0.0 to stop after the primary.
    /// </summary>
    [JsonPropertyName("stage3_waterfall_confidence_threshold")]
    public double Stage3WaterfallConfidenceThreshold { get; set; } = 0.65;

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
