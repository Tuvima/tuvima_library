using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// A specific physical manifestation of a <see cref="Work"/>
/// (e.g. "4K Blu-ray", "First Edition Hardcover", "Director's Cut").
///
/// An Edition is the bridge between the intellectual content (<see cref="Work"/>)
/// and the files on disk (<see cref="MediaAsset"/>).
/// One Work may have many Editions; one Edition may have many MediaAssets.
///
/// Metadata claims and canonical values can be scoped to an Edition as well as
/// to its parent Work, allowing edition-specific enrichment (e.g. runtime of a
/// specific cut differs from the theatrical release).
///
/// Maps to <c>editions</c> in the Phase 4 schema.
/// </summary>
public sealed class Edition
{
    /// <summary>Stable identifier. PK in <c>editions</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <c>works.id</c>.</summary>
    public Guid WorkId { get; set; }

    /// <summary>
    /// Human-readable label describing this physical form.
    /// Examples: <c>"4K Bluray"</c>, <c>"First Edition"</c>, <c>"Director's Cut"</c>.
    /// Nullable — may be absent before metadata is resolved.
    /// </summary>
    public string? FormatLabel { get; set; }

    /// <summary>
    /// Wikidata QID for this specific edition (e.g. Q113799157 for "Blade Runner: The Final Cut").
    /// Populated during Stage 2 (Wikidata Bridge Resolution) when the bridge ID resolves
    /// to an edition entity (P629 present).
    /// </summary>
    public string? WikidataQid { get; set; }

    // -------------------------------------------------------------------------
    // Children
    // -------------------------------------------------------------------------

    /// <summary>
    /// Physical files associated with this Edition.
    /// Each asset carries a verified <see cref="MediaAsset.ContentHash"/>.
    /// </summary>
    public List<MediaAsset> MediaAssets { get; set; } = [];

    // -------------------------------------------------------------------------
    // Metadata property bags
    // -------------------------------------------------------------------------

    /// <summary>
    /// All provider claims scoped to this Edition.
    /// Keyed by (<see cref="MetadataClaim.ClaimKey"/>, <see cref="MetadataClaim.ProviderId"/>).
    /// Append-only — NEVER remove historical claims.
    /// </summary>
    public List<MetadataClaim> MetadataClaims { get; set; } = [];

    /// <summary>
    /// Scored, authoritative values for this Edition derived from
    /// <see cref="MetadataClaims"/>.
    /// Keyed by <see cref="CanonicalValue.Key"/>.
    /// </summary>
    public List<CanonicalValue> CanonicalValues { get; set; } = [];
}
