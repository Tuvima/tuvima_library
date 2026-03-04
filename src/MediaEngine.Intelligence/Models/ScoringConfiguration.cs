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
}
