namespace MediaEngine.Domain.Constants;

/// <summary>
/// Named confidence bands that replace 15+ scattered raw thresholds.
/// Same behaviour, clearer code. Every threshold-based decision in the
/// pipeline should reference these bands instead of magic numbers.
/// </summary>
public static class ConfidenceBand
{
    /// <summary>Auto-accept, no review needed (bridge ID match, reconciliation title match).</summary>
    public const double ExactFloor       = 0.95;

    /// <summary>Auto-accept (retail auto-accept, organization without QID, "high confidence" display).</summary>
    public const double StrongFloor      = 0.85;

    /// <summary>Accept with review flag (retail ambiguous band).</summary>
    public const double ProvisionalFloor = 0.50;

    /// <summary>Low confidence — needs manual review.</summary>
    public const double AmbiguousFloor   = 0.30;

    // Below AmbiguousFloor = Insufficient — reject candidate.

    /// <summary>
    /// Classify a numeric confidence score into a named band.
    /// </summary>
    public static string Classify(double score) => score switch
    {
        >= ExactFloor       => "Exact",
        >= StrongFloor      => "Strong",
        >= ProvisionalFloor => "Provisional",
        >= AmbiguousFloor   => "Ambiguous",
        _                   => "Insufficient",
    };
}
