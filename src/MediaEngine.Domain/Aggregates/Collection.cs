using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// The aggregate root for a group of related <see cref="Work"/> instances.
///
/// A Collection is a virtual, intelligence-driven discovery collection. It answers
/// "what story-world does this belong to?" — powered by Wikidata relationship
/// properties (franchise, series, fictional universe, narrative chain).
///
/// Collections may optionally belong to a <see cref="Entities.Universe"/> via
/// <see cref="UniverseId"/>; a Collection MUST belong to at most one Universe.
///
/// Works without franchise/series/universe data are standalone (CollectionId = null).
/// Collection assignment is driven by Wikidata QID-to-QID matching (firm links) or
/// text commonality (provisional links) when no QID is available.
///
/// Maps to <c>collections</c> in the Phase 4 schema.
/// </summary>
public sealed class Collection
{
    /// <summary>Stable identifier. PK in <c>collections</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Optional membership in a <see cref="Entities.Universe"/>.
    /// Null when this Collection does not belong to any Universe.
    /// Spec: "a Collection MUST belong to a maximum of one Universe."
    /// </summary>
    public Guid? UniverseId { get; set; }

    /// <summary>
    /// Human-readable name for display in the Dashboard and folder structure.
    /// Set from the title canonical value at organization time, or from the
    /// library.xml sidecar during Great Inhale.
    /// Null on collections created before Phase 7.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Wikidata Q-identifier for the creative universe this Collection represents.
    /// Populated during Stage 1 (Reconciliation) of the hydration pipeline.
    /// Null until Wikidata identity has been confirmed.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>When this Collection was first registered in the system.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Wikidata coverage level for this Collection.
    /// Rich = QID found + 5+ properties filled.
    /// Limited = QID found but fewer than 5 properties.
    /// None = no QID found in Wikidata.
    /// Unknown = not yet checked (default for new/pre-existing collections).
    /// Tracked in DB and sidecar XML for filtering and scheduled refresh.
    /// </summary>
    public string UniverseStatus { get; set; } = "Unknown";

    /// <summary>
    /// Optional reference to a parent Collection that represents a franchise or creative universe.
    /// When set, this Collection is a "child" (e.g. "Dune Novels") of a broader Parent Collection (e.g. "Dune").
    /// Null for top-level Collections or Collections that don't belong to a larger franchise.
    /// </summary>
    public Guid? ParentCollectionId { get; set; }

    /// <summary>
    /// The type of collection container: Universe, Smart, System, Mix, Playlist, Genre, Author, Collection, or Custom.
    /// Defaults to "Universe" for backward compatibility.
    /// </summary>
    public string CollectionType { get; set; } = "Universe";

    /// <summary>Plain-text description or rule summary for display.</summary>
    public string? Description { get; set; }

    /// <summary>Icon name hint for UI rendering (e.g. "Label", "Waves", "Person").</summary>
    public string? IconName { get; set; }

    /// <summary>Local path to custom square artwork uploaded for this collection.</summary>
    public string? SquareArtworkPath { get; set; }

    /// <summary>MIME type for <see cref="SquareArtworkPath"/>.</summary>
    public string? SquareArtworkMimeType { get; set; }

    /// <summary>"library" for library-scoped collections, "user" for per-profile collections.</summary>
    public string Scope { get; set; } = "library";

    /// <summary>Owner profile. Null = library-scoped (shared).</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>Whether this collection is visible in browsing. Default true.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether this collection is pinned to media lane pages.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Minimum item count for smart collection generation threshold.</summary>
    public int MinItems { get; set; }

    /// <summary>JSON rule definition for smart collections (e.g. genre filter, vibe filter).</summary>
    public string? RuleJson { get; set; }

    /// <summary>How items are resolved: "query" (evaluate rules at display time) or "materialized" (pre-assigned).</summary>
    public string Resolution { get; set; } = "query";

    /// <summary>SHA-256 hash of normalized RuleJson for deduplication.</summary>
    public string? RuleHash { get; set; }

    /// <summary>For content groups: field to group results by (e.g. "season", "album").</summary>
    public string? GroupByField { get; set; }

    /// <summary>Rule match mode: "all" (AND) or "any" (OR).</summary>
    public string MatchMode { get; set; } = "all";

    /// <summary>Default sort field for collection results.</summary>
    public string? SortField { get; set; }

    /// <summary>Sort direction: "asc" or "desc".</summary>
    public string SortDirection { get; set; } = "desc";

    /// <summary>Whether query-resolved results auto-refresh when library changes.</summary>
    public bool LiveUpdating { get; set; } = true;

    /// <summary>Cron expression or descriptive schedule for mix refresh.</summary>
    public string? RefreshSchedule { get; set; }

    /// <summary>When the collection's contents were last refreshed/regenerated.</summary>
    public DateTimeOffset? LastRefreshedAt { get; set; }

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    // -------------------------------------------------------------------------
    // Children
    // -------------------------------------------------------------------------

    /// <summary>
    /// All Works that belong to this Collection.
    /// This is the aggregate boundary: changes to a Collection and its Works
    /// MUST occur within a single transaction.
    /// </summary>
    public List<Work> Works { get; set; } = [];

    /// <summary>
    /// Multi-dimensional grouping signals from Wikidata that define this Collection.
    /// Each relationship links to a Wikidata QID (franchise, series, universe, etc.).
    /// </summary>
    public List<CollectionRelationship> Relationships { get; set; } = [];

    /// <summary>
    /// Child Collections that belong to this Collection as a franchise/universe parent.
    /// Only populated when this Collection acts as a Parent Collection.
    /// Not loaded by default — requires explicit query.
    /// </summary>
    public List<Collection> ChildCollections { get; set; } = [];
}
