using System.Text.Json.Serialization;

namespace Tanaste.Storage.Models;

/// <summary>
/// Configuration for the three-stage hydration pipeline.
///
/// Loaded from <c>config/hydration.json</c>. Controls concurrency, timeouts,
/// disambiguation thresholds, and the confidence gate that triggers review
/// queue entries.
/// </summary>
public sealed class HydrationSettings
{
    /// <summary>
    /// Maximum concurrent provider calls within each stage.
    /// Shared across all providers in a given stage.
    /// </summary>
    [JsonPropertyName("stage_concurrency")]
    public int StageConcurrency { get; set; } = 3;

    /// <summary>Timeout in seconds for Stage 1 (Retail Match) provider calls.</summary>
    [JsonPropertyName("stage1_timeout_seconds")]
    public int Stage1TimeoutSeconds { get; set; } = 30;

    /// <summary>Timeout in seconds for Stage 2 (Universal Bridge) provider calls.</summary>
    [JsonPropertyName("stage2_timeout_seconds")]
    public int Stage2TimeoutSeconds { get; set; } = 45;

    /// <summary>Timeout in seconds for Stage 3 (Human Hub) provider calls.</summary>
    [JsonPropertyName("stage3_timeout_seconds")]
    public int Stage3TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Minimum confidence required for a QID match to be accepted automatically
    /// during Stage 2 disambiguation. Below this threshold, multiple candidates
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
    /// When <c>true</c>, Stage 2 is skipped if Stage 1 did not deposit any
    /// bridge identifiers (ISBN, ASIN, TMDB ID, etc.) as canonical values.
    /// When <c>false</c>, Stage 2 falls back to title-based search.
    /// </summary>
    [JsonPropertyName("skip_stage2_without_bridge_ids")]
    public bool SkipStage2WithoutBridgeIds { get; set; }
}
