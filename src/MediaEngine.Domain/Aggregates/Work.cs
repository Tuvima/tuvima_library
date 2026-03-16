using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// The intellectual representation of a single title — the "what" of the media,
/// independent of any specific physical copy or encoding.
///
/// A Work belongs to exactly one <see cref="Hub"/> (its aggregate root).
/// It may have many <see cref="Edition"/> children, each representing a distinct
/// physical form of the same content.
///
/// Spec invariants:
/// • Standalone Works (no franchise/series/universe data) have HubId = null.
/// • "A Work linked to a Series MUST contain a SequenceIndex." — enforced at
///   the application layer using <see cref="SequenceIndex"/>.
///
/// Maps to <c>works</c> in the Phase 4 schema.
/// </summary>
public sealed class Work
{
    /// <summary>Stable identifier. PK in <c>works</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Parent Hub. Nullable: Works without franchise/series/universe data
    /// are standalone. Hub assignment is driven by Wikidata relationship
    /// properties during Stage 2 of the hydration pipeline.
    /// </summary>
    public Guid? HubId { get; set; }

    /// <summary>
    /// Wikidata Q-identifier for the specific work (book, film, etc.).
    /// Populated during Stage 1 (Reconciliation) of the hydration pipeline.
    /// Null until Wikidata identity has been confirmed for this Work.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>
    /// The kind of intellectual content this Work contains.
    /// Stored as a string discriminator (<c>media_type</c> TEXT) in the database.
    /// </summary>
    public MediaType MediaType { get; set; }

    /// <summary>
    /// Position of this Work within an ordered series.
    /// MUST be set when the parent Hub represents a series.
    /// Null for standalone works.
    /// Spec: "A Work linked to a Series MUST contain a SequenceIndex."
    /// </summary>
    public int? SequenceIndex { get; set; }

    // -------------------------------------------------------------------------
    // Universe matching state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Indicates the user explicitly skipped Universe (Wikidata) matching for
    /// this Work. When <c>true</c>, the pipeline will not attempt Stage 2
    /// universe linking and the Work is treated as content-matched only.
    /// Stored as INTEGER (0/1) in the <c>works</c> table.
    /// </summary>
    public bool UniverseMismatch { get; set; }

    /// <summary>
    /// Timestamp when <see cref="UniverseMismatch"/> was set to <c>true</c>.
    /// Null when universe matching has not been skipped.
    /// </summary>
    public DateTimeOffset? UniverseMismatchAt { get; set; }

    /// <summary>
    /// Wikidata lookup status: "confirmed" (QID found, firm link),
    /// "pending" (no QID yet, recheck periodically), "skipped" (user decision).
    /// </summary>
    public string WikidataStatus { get; set; } = "pending";

    /// <summary>
    /// Timestamp of the last Wikidata lookup attempt.
    /// Used by the weekly sync to prioritize pending items for recheck.
    /// </summary>
    public DateTimeOffset? WikidataCheckedAt { get; set; }

    // -------------------------------------------------------------------------
    // Children
    // -------------------------------------------------------------------------

    /// <summary>
    /// All known physical editions of this Work (e.g. theatrical vs. director's cut).
    /// </summary>
    public List<Edition> Editions { get; set; } = [];

    // -------------------------------------------------------------------------
    // Metadata property bags
    // -------------------------------------------------------------------------

    /// <summary>
    /// All provider-asserted key-value claims about this Work.
    /// Multiple providers may assert values for the same key with differing
    /// <see cref="MetadataClaim.Confidence"/> levels.
    /// Append-only — historical claims are never removed.
    /// </summary>
    public List<MetadataClaim> MetadataClaims { get; set; } = [];

    /// <summary>
    /// The winning metadata values for this Work after the scoring engine has
    /// resolved competing claims.
    /// Each entry represents one resolved field in the property bag.
    /// </summary>
    public List<CanonicalValue> CanonicalValues { get; set; } = [];
}
