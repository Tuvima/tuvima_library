namespace MediaEngine.Intelligence.Models;

/// <summary>
/// The output of a single <see cref="Contracts.IIdentityMatcher.MatchAsync"/> call.
///
/// Spec: Phase 6 – Identity Matching; Short-Circuit Evaluation.
/// </summary>
public sealed class MatchResult
{
    /// <summary>
    /// Overall similarity between the two entities.
    /// 1.0 = identical (or hard-identifier match), 0.0 = completely different.
    /// </summary>
    public required double Similarity { get; init; }

    /// <summary>
    /// Names of the hard-identifier keys (e.g. <c>"isbn"</c>, <c>"imdbid"</c>)
    /// that matched between the two entities.  Empty when no hard match was found.
    /// </summary>
    public IReadOnlyList<string> MatchedIdentifiers { get; init; } = [];

    /// <summary>
    /// <see langword="true"/> when at least one hard identifier (ISBN, IMDb, etc.)
    /// matched, short-circuiting the fuzzy comparison.
    /// Spec: Phase 6 – Scalability § Short-Circuit Evaluation.
    /// </summary>
    public bool HardIdentifierMatch { get; init; }

    /// <summary>
    /// Disposition derived by comparing <see cref="Similarity"/> against the
    /// configured <see cref="ScoringConfiguration.AutoLinkThreshold"/> and
    /// <see cref="ScoringConfiguration.ConflictThreshold"/>.
    /// </summary>
    public required LinkDisposition Disposition { get; init; }
}
