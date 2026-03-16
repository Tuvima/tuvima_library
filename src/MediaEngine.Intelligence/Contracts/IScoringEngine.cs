using MediaEngine.Intelligence.Models;

namespace MediaEngine.Intelligence.Contracts;

/// <summary>
/// Entry point for the Intelligence &amp; Scoring Engine.
///
/// Iterates over all claim keys present in a <see cref="ScoringContext"/>,
/// applies priority cascade logic to each field, and aggregates
/// per-field scores into a single <see cref="ScoringResult"/>.
///
/// ──────────────────────────────────────────────────────────────────
/// Determinism guarantee (spec: Phase 6 – Deterministic Logic)
/// ──────────────────────────────────────────────────────────────────
///  Given identical <see cref="ScoringContext"/> inputs (same claims, same
///  weights, same thresholds), the engine MUST produce an identical
///  <see cref="ScoringResult"/>.  No randomness or external I/O is permitted.
///
/// ──────────────────────────────────────────────────────────────────
/// Provider agnosticism (spec: Phase 6 – Provider Agnosticism)
/// ──────────────────────────────────────────────────────────────────
///  The engine has no knowledge of specific provider names or semantics.
///  It interacts exclusively with <see cref="MediaEngine.Domain.Entities.MetadataClaim"/>
///  records and numeric provider weights supplied via <see cref="ScoringContext"/>.
///
/// Spec: Phase 6 – Intelligence § IScoringEngine.
/// </summary>
public interface IScoringEngine
{
    /// <summary>
    /// Scores all metadata claims for a single entity and returns the resolved
    /// field values together with an overall confidence score.
    /// </summary>
    /// <param name="context">
    /// All claims for the entity, plus provider weights and scoring configuration.
    /// </param>
    Task<ScoringResult> ScoreEntityAsync(ScoringContext context, CancellationToken ct = default);

    /// <summary>
    /// Convenience overload for batch scoring.
    /// Implementations SHOULD process contexts concurrently up to the system's
    /// configured parallelism limit.
    /// </summary>
    Task<IReadOnlyList<ScoringResult>> ScoreBatchAsync(
        IEnumerable<ScoringContext> contexts,
        CancellationToken ct = default);
}
