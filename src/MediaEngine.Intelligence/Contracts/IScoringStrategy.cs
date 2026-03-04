namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Extension point for pluggable string-comparison algorithms used by
/// <see cref="IConflictResolver"/> when computing per-field confidence scores.
///
/// ──────────────────────────────────────────────────────────────────
/// Strategy selection (spec: Phase 6 – IScoringStrategy extension point)
/// ──────────────────────────────────────────────────────────────────
///  Strategies are selected at runtime by their <see cref="AppliesTo"/> predicate.
///  When multiple strategies match a key, <see cref="IConflictResolver"/> uses
///  the one registered first (implementations decide order).
///
///  Built-in strategies:
///   • <c>LevenshteinStrategy</c>  — normalised edit-distance for free-text fields
///   • <c>ExactMatchStrategy</c>   — strict equality for hard identifiers (ISBN, IMDb)
///
/// Spec: Phase 6 – Extension Points § IScoringStrategy.
/// </summary>
public interface IScoringStrategy
{
    /// <summary>Stable name used for logging and diagnostics (e.g. "Levenshtein", "ExactMatch").</summary>
    string Name { get; }

    /// <summary>
    /// Returns <see langword="true"/> when this strategy is the appropriate
    /// comparator for <paramref name="claimKey"/>.
    /// </summary>
    bool AppliesTo(string claimKey);

    /// <summary>
    /// Computes the similarity between two claim values.
    /// </summary>
    /// <param name="a">Normalised value from one provider.</param>
    /// <param name="b">Normalised value from another provider (or the candidate).</param>
    /// <returns>Similarity score in [0.0, 1.0]; 1.0 = identical, 0.0 = completely different.</returns>
    double Compute(string a, string b);
}
