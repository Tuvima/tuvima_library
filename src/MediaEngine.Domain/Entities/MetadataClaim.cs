namespace MediaEngine.Domain.Entities;

/// <summary>
/// A single key-value claim made by one metadata provider about a domain entity.
///
/// Together, all claims for a given entity form the <em>property bag</em> —
/// an open-ended collection of provider-asserted facts that the scoring engine
/// later resolves into <see cref="CanonicalValue"/> records.
///
/// Maps 1:1 to a row in the <c>metadata_claims</c> table.
/// Rows are NEVER deleted; the full claim history is retained to allow
/// re-scoring when provider weights change.
/// Spec: Phase 4 – Invariants § Claim History.
/// </summary>
public sealed class MetadataClaim
{
    /// <summary>Stable row identifier (UUID → TEXT in SQLite).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Polymorphic foreign key — points to either a <c>Work.Id</c> or an
    /// <c>Edition.Id</c>.  SQLite cannot enforce this at the DB level;
    /// the application layer is responsible for the correct target type.
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// The provider that asserted this claim.
    /// FK → <c>provider_registry.id</c>.
    /// </summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// The metadata field name this claim pertains to.
    /// Examples: <c>"title"</c>, <c>"release_year"</c>, <c>"genre"</c>.
    /// Forms one half of the key-value property bag.
    /// </summary>
    public string ClaimKey { get; set; } = string.Empty;

    /// <summary>
    /// The provider's asserted value for <see cref="ClaimKey"/>.
    /// Always serialised as a string; the scoring engine interprets the type.
    /// Forms the other half of the key-value property bag.
    /// </summary>
    public string ClaimValue { get; set; } = string.Empty;

    /// <summary>
    /// Provider-supplied confidence in this claim.
    /// Range: 0.0 – 1.0.  Defaults to 1.0 (fully confident).
    /// Used by the scoring engine; not validated here.
    /// </summary>
    public double Confidence { get; set; } = 1.0;

    /// <summary>
    /// When this claim was first asserted.
    /// Defaults to <see cref="DateTimeOffset.UtcNow"/> at construction time.
    /// The scoring engine uses this to apply a time-decay factor to claims
    /// significantly older than their peers (spec: Phase 6 – Stale Claim Handling).
    /// Maps to <c>metadata_claims.claimed_at</c> (ISO-8601 TEXT in SQLite).
    /// </summary>
    public DateTimeOffset ClaimedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When <c>true</c>, the scoring engine treats this claim as the unconditional
    /// winner for its field (confidence 1.0) and ignores all competing claims.
    ///
    /// This flag is ONLY set by explicit user action (a manual metadata override).
    /// No automated processor, file scanner, or external provider may set it.
    ///
    /// Spec: Phase 8 – Field-Level Arbitration § User-Locked Claims.
    /// Maps to <c>metadata_claims.is_user_locked</c> (INTEGER 0/1 in SQLite).
    /// </summary>
    public bool IsUserLocked { get; set; } = false;
}
