using MediaEngine.Domain.Entities;
using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Determines the probability that two domain entities represent the same
/// intellectual work by comparing their canonical metadata.
///
/// ──────────────────────────────────────────────────────────────────
/// Matching algorithm (spec: Phase 6 – Identity Matching)
/// ──────────────────────────────────────────────────────────────────
///  1. Hard-identifier short-circuit: if both entities share at least one
///     matching value for a hard-identifier key (ISBN, IMDbID, TMDbID, EAN, ASIN)
///     the match is immediately returned with <c>Similarity = 1.0</c> and
///     <c>HardIdentifierMatch = true</c>.
///     Spec: "Matching of 'Hard Identifiers' MAY bypass fuzzy-logic comparisons."
///
///  2. Field-level similarity: for each key present in both entities,
///     an <see cref="IScoringStrategy"/> is applied (Levenshtein for text,
///     ExactMatch for structured IDs) and a weighted average is computed.
///     The <c>"title"</c> key receives elevated weight (0.5) to reflect its
///     high discriminating power.
///
/// Spec: Phase 6 – Intelligence § IIdentityMatcher.
/// </summary>
public interface IIdentityMatcher
{
    /// <summary>
    /// Compares two sets of canonical values representing distinct entities.
    /// </summary>
    /// <param name="entity">
    /// Canonical values for the entity being evaluated (e.g. a newly ingested Work).
    /// </param>
    /// <param name="candidate">
    /// Canonical values for the candidate entity (e.g. an existing Work in a Collection).
    /// </param>
    /// <param name="configuration">
    /// Scoring configuration supplying the auto-link and conflict thresholds
    /// used to determine the <see cref="MatchResult.Disposition"/>.
    /// </param>
    /// <param name="ct">Forwarded to async I/O if needed by future implementations.</param>
    Task<MatchResult> MatchAsync(
        IEnumerable<CanonicalValue> entity,
        IEnumerable<CanonicalValue> candidate,
        ScoringConfiguration configuration,
        CancellationToken ct = default);
}
