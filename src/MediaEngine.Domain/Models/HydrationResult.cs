namespace MediaEngine.Domain.Models;

/// <summary>
/// Result of running the three-stage hydration pipeline for a single entity.
///
/// Returned by <see cref="Contracts.IHydrationPipelineService.RunSynchronousAsync"/>
/// and used by the API layer to build detailed hydration responses.
/// </summary>
public sealed class HydrationResult
{
    /// <summary>Number of claims added during Stage 1 (Authority Match — Wikidata).</summary>
    public int Stage1ClaimsAdded { get; set; }

    /// <summary>Number of claims added during Stage 2 (Context Match — Wikipedia).</summary>
    public int Stage2ClaimsAdded { get; set; }

    /// <summary>Number of claims added during Stage 3 (Retail Match — waterfall).</summary>
    public int Stage3ClaimsAdded { get; set; }

    /// <summary>Total claims added across all three stages.</summary>
    public int TotalClaimsAdded => Stage1ClaimsAdded + Stage2ClaimsAdded + Stage3ClaimsAdded;

    /// <summary>
    /// The Wikidata QID resolved during Stage 1 (nullable).
    /// Null if Stage 1 did not find a match or was skipped.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>
    /// Whether the pipeline created a review queue entry because it could not
    /// proceed automatically (disambiguation needed or low confidence).
    /// </summary>
    public bool NeedsReview { get; set; }

    /// <summary>
    /// Human-readable reason for the review flag (nullable).
    /// Corresponds to a <see cref="Enums.ReviewTrigger"/> constant.
    /// </summary>
    public string? ReviewReason { get; set; }

    /// <summary>
    /// The ID of the review queue entry created, if <see cref="NeedsReview"/> is true.
    /// </summary>
    public Guid? ReviewItemId { get; set; }

    /// <summary>
    /// QID candidates returned by Wikidata when disambiguation is needed (nullable).
    /// Present when <see cref="ReviewReason"/> is
    /// <see cref="Enums.ReviewTrigger.MultipleQidMatches"/>.
    /// </summary>
    public IReadOnlyList<QidCandidate>? DisambiguationCandidates { get; set; }
}

/// <summary>
/// A single Wikidata QID candidate for disambiguation.
///
/// Returned by the WikidataAdapter when a bridge lookup produces multiple
/// possible matches. The user selects one to proceed with Stage 2+3 hydration.
/// </summary>
public sealed class QidCandidate
{
    /// <summary>The Wikidata Q-identifier (e.g. "Q190192").</summary>
    public required string Qid { get; init; }

    /// <summary>The English label for the entity (e.g. "Dune").</summary>
    public required string Label { get; init; }

    /// <summary>Optional description from Wikidata (e.g. "1965 novel by Frank Herbert").</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The resolution tier that produced this candidate (e.g. "bridge", "structured_sparql",
    /// "title_search"). Used by the disambiguation UI to show how each candidate was found.
    /// Null for candidates from legacy code paths.
    /// </summary>
    public string? ResolutionTier { get; init; }
}
