namespace MediaEngine.Intelligence.Models;

/// <summary>
/// Immutable snapshot of the scoring thresholds and decay parameters consumed
/// by the Intelligence &amp; Scoring Engine at runtime.
///
/// Typically constructed from <see cref="MediaEngine.Storage.Models.ScoringSettings"/>
/// at startup so that the Intelligence layer does not take a hard dependency on
/// the Storage manifest model.
///
/// Spec: Phase 6 – Threshold Enforcement; Weight Management; Stale Claim Handling.
/// </summary>
public sealed class ScoringConfiguration
{
    /// <summary>
    /// Minimum overall confidence for a Work → Hub link to be applied
    /// automatically.  Range: (0.0, 1.0].  Default: 0.85.
    /// </summary>
    public double AutoLinkThreshold { get; init; } = 0.85;

    /// <summary>
    /// Works scoring between this value and <see cref="AutoLinkThreshold"/>
    /// are flagged <see cref="LinkDisposition.NeedsReview"/>.
    /// Range: [0.0, <see cref="AutoLinkThreshold"/>).  Default: 0.60.
    /// </summary>
    public double ConflictThreshold { get; init; } = 0.60;

    /// <summary>
    /// A field is considered "conflicted" when the runner-up value's normalised
    /// weight is within this fraction of the winner's weight.
    /// E.g. 0.05 means "within 5 % of the winner."  Default: 0.05.
    /// </summary>
    public double ConflictEpsilon { get; init; } = 0.05;

    /// <summary>
    /// Claims older than this many days are considered stale.
    /// Zero disables stale-claim decay entirely.
    /// </summary>
    public int StaleClaimDecayDays { get; init; } = 90;

    /// <summary>
    /// Weight multiplier applied to stale claims (0.0, 1.0].
    /// A value of 1.0 effectively disables decay.  Default: 0.8.
    /// </summary>
    public double StaleClaimDecayFactor { get; init; } = 0.8;

    /// <summary>
    /// Confidence score at or above which the UI shows the green/high confidence indicator.
    /// Must be ≤ 1.0 and ≥ <see cref="ConfidenceDisplayMedium"/>.
    /// Default: 0.85 (matches <see cref="AutoLinkThreshold"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("confidence_display_high")]
    public double ConfidenceDisplayHigh { get; init; } = 0.85;

    /// <summary>
    /// Confidence score at or above which the UI shows the amber/medium confidence indicator
    /// (below <see cref="ConfidenceDisplayHigh"/>).
    /// Default: 0.60 (matches <see cref="ConflictThreshold"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("confidence_display_medium")]
    public double ConfidenceDisplayMedium { get; init; } = 0.60;

    /// <summary>
    /// Per-media-type confidence floor configuration.
    /// When ALL critical fields for a media type score at or above the minimum,
    /// the floor boost is added to overall confidence (capped at 1.0).
    /// Keys are <see cref="MediaEngine.Domain.Enums.MediaType"/> string names.
    /// </summary>
    public IReadOnlyDictionary<string, ConfidenceFloor> ConfidenceFloors { get; init; }
        = DefaultConfidenceFloors;

    /// <summary>
    /// Built-in defaults: if all critical fields for the media type meet the
    /// minimum score, +0.10 is added to overall confidence.
    /// </summary>
    public static IReadOnlyDictionary<string, ConfidenceFloor> DefaultConfidenceFloors { get; } =
        new Dictionary<string, ConfidenceFloor>(StringComparer.OrdinalIgnoreCase)
        {
            ["Books"]      = new(["title", "author"], 0.70, 0.10),
            ["Audiobooks"] = new(["title", "author"], 0.70, 0.10),
            ["Movies"]     = new(["title", "year"],   0.70, 0.15),
            ["TV"]         = new(["title"],            0.70, 0.15),
            ["Music"]      = new(["title", "artist"],  0.70, 0.10),
            ["Comic"]      = new(["title"],            0.70, 0.10),
        };
}

/// <summary>
/// Defines the confidence floor rule for a media type.
/// If ALL <see cref="CriticalFields"/> score at or above <see cref="MinFieldScore"/>,
/// <see cref="FloorBoost"/> is added to overall confidence.
/// </summary>
/// <param name="CriticalFields">Field keys that must all meet the threshold.</param>
/// <param name="MinFieldScore">Minimum per-field confidence to qualify (0.0–1.0).</param>
/// <param name="FloorBoost">Additive boost to overall confidence (0.0–1.0).</param>
public record ConfidenceFloor(
    IReadOnlyList<string> CriticalFields,
    double MinFieldScore,
    double FloorBoost);
