using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

/// <summary>
/// Mutable context object that accumulates evidence as the pipeline processes
/// a single asset. Each stage writes into it; the decision service reads it
/// to produce a final <see cref="IdentityDecision"/> verdict.
///
/// The context is created at the start of the identity pipeline and discarded
/// once the decision has been acted on. It is never persisted directly — the
/// persisted artefacts (claims, candidates, review entries) are written by the
/// individual workers.
/// </summary>
public sealed class IdentityResolutionContext
{
    // ── Identity ─────────────────────────────────────────────────────────────

    /// <summary>The media asset entity being resolved.</summary>
    public Guid EntityId { get; init; }

    /// <summary>The media type of the asset (e.g. Books, Movies, TV).</summary>
    public MediaType MediaType { get; init; }

    // ── File evidence ────────────────────────────────────────────────────────

    /// <summary>
    /// Raw metadata claims extracted from the file by the processor.
    /// These are the un-scored, un-merged facts the pipeline starts with.
    /// </summary>
    public IReadOnlyList<MetadataClaim> FileMetadataClaims { get; set; } = [];

    /// <summary>
    /// Canonical values as resolved by the scoring engine after file claims
    /// have been evaluated. Keyed by field name (e.g. "title", "author").
    /// Populated after Stage 1 scoring; may be updated after Stage 2.
    /// </summary>
    public IReadOnlyDictionary<string, string> CanonicalValues { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // ── Retail evidence ──────────────────────────────────────────────────────

    /// <summary>
    /// All scored retail candidates returned by Stage 1 providers, ordered
    /// by descending composite score.
    /// </summary>
    public IReadOnlyList<RetailMatchCandidate> RetailCandidates { get; set; } = [];

    /// <summary>
    /// The highest-scoring retail candidate, or <c>null</c> if Stage 1
    /// returned no candidates above the minimum threshold.
    /// </summary>
    public RetailMatchCandidate? BestRetailCandidate { get; set; }

    /// <summary>
    /// Composite confidence score for <see cref="BestRetailCandidate"/> (0.0–1.0).
    /// 0.0 when no retail candidate was found.
    /// </summary>
    public double RetailScore { get; set; }

    /// <summary>
    /// Named confidence band for <see cref="RetailScore"/>.
    /// Computed via <see cref="ConfidenceBand.Classify"/> — one of:
    /// "Exact", "Strong", "Provisional", "Ambiguous", "Insufficient".
    /// </summary>
    public string RetailBand => ConfidenceBand.Classify(RetailScore);

    // ── Bridge evidence ──────────────────────────────────────────────────────

    /// <summary>
    /// External identifier keys and values extracted from the best retail
    /// candidate (e.g. "isbn" → "9780441172719", "tmdb_id" → "438631").
    /// Passed to Stage 2 for precise Wikidata QID resolution.
    /// Empty when no retail candidate was found.
    /// </summary>
    public IReadOnlyDictionary<string, string> BridgeIds { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // ── Wikidata evidence ────────────────────────────────────────────────────

    /// <summary>
    /// The Wikidata Q-identifier resolved by Stage 2, or <c>null</c> if
    /// Wikidata resolution was not attempted or did not succeed.
    /// Example: "Q190192".
    /// </summary>
    public string? ResolvedQid { get; set; }

    /// <summary>
    /// How the QID was resolved. One of: "bridge", "text", "album".
    /// Null when no QID was resolved.
    /// </summary>
    public string? ResolutionMethod { get; set; }

    /// <summary>
    /// Property claims fetched from Wikidata after QID resolution.
    /// These are the authoritative claims that override retail data in the
    /// Priority Cascade (Tier C).
    /// </summary>
    public IReadOnlyList<MetadataClaim> WikidataClaims { get; set; } = [];

    // ── Prior state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Existing review queue entries for this entity, loaded at the start of
    /// the pipeline run. Used to avoid creating duplicate review items and to
    /// carry context from prior pipeline attempts.
    /// </summary>
    public IReadOnlyList<ReviewQueueEntry> PriorReviews { get; set; } = [];

    /// <summary>
    /// Whether the entity has any user-locked metadata fields.
    /// When <c>true</c>, the decision service applies Tier A rules and skips
    /// automated overrides for locked fields.
    /// </summary>
    public bool HasUserLocks { get; set; }

    // ── Decision output ──────────────────────────────────────────────────────

    /// <summary>
    /// The verdict produced by the decision service after evaluating all
    /// accumulated evidence. Defaults to <see cref="IdentityDecision.Review"/>
    /// until the decision service runs.
    /// </summary>
    public IdentityDecision Decision { get; set; } = IdentityDecision.Review;

    /// <summary>
    /// The root cause to attach to the review item when
    /// <see cref="Decision"/> is <see cref="IdentityDecision.Review"/>.
    /// Null for all other decisions.
    /// </summary>
    public ReviewRootCause? ReviewCause { get; set; }
}
