using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// The intellectual representation of a single title — the "what" of the media,
/// independent of any specific physical copy or encoding.
///
/// A Work may have many <see cref="Edition"/> children, each representing a
/// distinct physical form of the same content. Works also form a parent/child
/// hierarchy among themselves (introduced in M-081): an album is a parent
/// Work whose tracks are child Works, a TV show is a parent of seasons which
/// are parents of episodes, and so on.
///
/// Maps to <c>works</c> in the database.
/// </summary>
public sealed class Work
{
    /// <summary>Stable identifier. PK in <c>works</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Optional Hub for legacy ContentGroup grouping. Phase 4 collapses this
    /// onto the new <see cref="ParentWorkId"/> hierarchy and the column is
    /// expected to disappear in a later migration.
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

    // -------------------------------------------------------------------------
    // M-081 — parent/child hierarchy
    // -------------------------------------------------------------------------

    /// <summary>
    /// This Work's role in the parent/child hierarchy.
    /// See <see cref="WorkKind"/> for the four roles. Default is
    /// <see cref="WorkKind.Standalone"/>.
    /// Stored as TEXT in <c>works.work_kind</c> with a CHECK constraint.
    /// </summary>
    public WorkKind WorkKind { get; set; } = WorkKind.Standalone;

    /// <summary>
    /// The parent Work in the hierarchy. NULL for
    /// <see cref="WorkKind.Standalone"/> and root <see cref="WorkKind.Parent"/>
    /// rows; set for every <see cref="WorkKind.Child"/> and for any nested
    /// parent (e.g. a Season whose parent is a Show).
    /// FK on <c>works.parent_work_id</c> with ON DELETE SET NULL.
    /// </summary>
    public Guid? ParentWorkId { get; set; }

    /// <summary>
    /// Position of this Work within its parent — track number, episode
    /// number, issue number, volume number. NULL for standalone Works
    /// and for parent containers themselves.
    /// Replaces the legacy <c>sequence_index</c> column.
    /// </summary>
    public int? Ordinal { get; set; }

    /// <summary>
    /// True when this Work is known to exist externally (Wikidata or a
    /// retail provider) but no file is in the library yet. Catalog Works
    /// power the "show me what I'm missing" view and are promoted to
    /// <see cref="WorkKind.Standalone"/> or <see cref="WorkKind.Child"/>
    /// when their files are ingested.
    /// </summary>
    public bool IsCatalogOnly { get; set; }

    /// <summary>
    /// Provider-specific identifiers for this Work (e.g.
    /// <c>{"isbn_13":"...", "tmdb_id":"...", "apple_music_id":"..."}</c>).
    /// Stored as a JSON blob in <c>works.external_identifiers</c>.
    /// Used by the HierarchyResolver to re-find catalog rows when their
    /// files are later ingested, and by the enrichment pipeline to skip
    /// search and call providers directly.
    /// </summary>
    public Dictionary<string, string> ExternalIdentifiers { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalized find-or-create cache key. Populated only on parent Works
    /// (<see cref="WorkKind.Parent"/>). Format is media-type-specific and
    /// computed by <c>HierarchyResolver</c> — e.g.
    /// <c>"pink floyd|the dark side of the moon"</c> for music albums or
    /// <c>"breaking bad"</c> for TV shows.
    /// Backed by <c>works.parent_key</c> with a partial index on
    /// <c>(media_type, parent_key)</c>. NULL on standalone/child/catalog rows.
    /// </summary>
    public string? ParentKey { get; set; }

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

    /// <summary>
    /// Match resolution level — drives the status chip on the Details tab:
    /// <list type="bullet">
    ///   <item><c>"edition"</c> — <b>Fully Linked</b> (green). Matched to a specific
    ///     Wikidata edition AND its parent work. Best case: edition-specific cover,
    ///     narrator, format metadata, plus full universe linkage.</item>
    ///   <item><c>"work"</c> — <b>Linked</b> (blue). Matched to the Wikidata work
    ///     but no edition entity found. Universe, franchise, characters all work.
    ///     For edition-aware media types, periodically re-checked.</item>
    ///   <item><c>"retail_only"</c> — <b>Identified</b> (amber). Retail provider matched
    ///     (cover + description + bridge IDs) but Wikidata has no entity for this item.
    ///     Will be regularly re-checked. Fully usable but no universe linkage.</item>
    ///   <item><c>"unlinked"</c> — <b>Unlinked</b> (grey). No external match found at all.
    ///     File metadata only. Very rare edge case.</item>
    /// </list>
    /// </summary>
    public string MatchLevel { get; set; } = "work";

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
