namespace MediaEngine.Domain.Entities;

/// <summary>
/// The authoritative, scored value for a single metadata key on a domain entity.
///
/// Produced by the scoring engine after it resolves competing
/// <see cref="MetadataClaim"/> records for the same key.
/// Every record here MUST be derivable from one or more claims in
/// <c>metadata_claims</c> (spec: Canonical Integrity invariant).
///
/// Maps 1:1 to a row in the <c>canonical_values</c> table.
/// The composite primary key is (EntityId, Key).
/// </summary>
public sealed class CanonicalValue
{
    /// <summary>
    /// Polymorphic owner — points to either a <c>Work.Id</c> or an
    /// <c>Edition.Id</c>, matching the same entity as the underlying claims.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>Metadata field name, e.g. <c>"title"</c>, <c>"release_year"</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The winning value after scoring.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// When the scoring engine last computed this value.
    /// Used to detect staleness when provider weights are updated.
    /// </summary>
    public DateTimeOffset LastScoredAt { get; set; }

    /// <summary>
    /// <see langword="true"/> when the scoring engine could not pick a clear winner
    /// for this field — the runner-up value's weight was within epsilon of the
    /// winner's weight.  Surfaced in the Dashboard so a Curator can resolve it
    /// manually via the lock-claim endpoint.
    /// Spec: Phase B – Conflict Surfacing (B-05).
    /// </summary>
    public bool IsConflicted { get; set; }

    /// <summary>
    /// The provider that supplied the winning claim for this field.
    /// Null when no provider weight information was available (e.g. the value
    /// came from a user lock or was scored before this column was added).
    /// Spec: Unit 4 – Source Attribution.
    /// </summary>
    public Guid? WinningProviderId { get; set; }

    /// <summary>
    /// <see langword="true"/> when this field requires human review — either because
    /// the scoring engine flagged it as conflicted, the field is missing an expected
    /// value after hydration, or the winning claim came solely from the local
    /// filesystem provider with low overall confidence.
    /// Spec: Unit 5 – Per-Field NeedsReview / Self-Healing Logic.
    /// </summary>
    public bool NeedsReview { get; set; }
}
