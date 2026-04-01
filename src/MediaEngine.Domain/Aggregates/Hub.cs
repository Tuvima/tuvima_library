using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Aggregates;

/// <summary>
/// The aggregate root for a group of related <see cref="Work"/> instances.
///
/// A Hub is a virtual, intelligence-driven discovery collection. It answers
/// "what story-world does this belong to?" — powered by Wikidata relationship
/// properties (franchise, series, fictional universe, narrative chain).
///
/// Hubs may optionally belong to a <see cref="Entities.Universe"/> via
/// <see cref="UniverseId"/>; a Hub MUST belong to at most one Universe.
///
/// Works without franchise/series/universe data are standalone (HubId = null).
/// Hub assignment is driven by Wikidata QID-to-QID matching (firm links) or
/// text commonality (provisional links) when no QID is available.
///
/// Maps to <c>hubs</c> in the Phase 4 schema.
/// </summary>
public sealed class Hub
{
    /// <summary>Stable identifier. PK in <c>hubs</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Optional membership in a <see cref="Entities.Universe"/>.
    /// Null when this Hub does not belong to any Universe.
    /// Spec: "a Hub MUST belong to a maximum of one Universe."
    /// </summary>
    public Guid? UniverseId { get; set; }

    /// <summary>
    /// Human-readable name for display in the Dashboard and folder structure.
    /// Set from the title canonical value at organization time, or from the
    /// library.xml sidecar during Great Inhale.
    /// Null on hubs created before Phase 7.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Wikidata Q-identifier for the creative universe this Hub represents.
    /// Populated during Stage 1 (Reconciliation) of the hydration pipeline.
    /// Null until Wikidata identity has been confirmed.
    /// </summary>
    public string? WikidataQid { get; set; }

    /// <summary>When this Hub was first registered in the system.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Wikidata coverage level for this Hub.
    /// Rich = QID found + 5+ properties filled.
    /// Limited = QID found but fewer than 5 properties.
    /// None = no QID found in Wikidata.
    /// Unknown = not yet checked (default for new/pre-existing hubs).
    /// Tracked in DB and sidecar XML for filtering and scheduled refresh.
    /// </summary>
    public string UniverseStatus { get; set; } = "Unknown";

    /// <summary>
    /// Optional reference to a parent Hub that represents a franchise or creative universe.
    /// When set, this Hub is a "child" (e.g. "Dune Novels") of a broader Parent Hub (e.g. "Dune").
    /// Null for top-level Hubs or Hubs that don't belong to a larger franchise.
    /// </summary>
    public Guid? ParentHubId { get; set; }

    /// <summary>
    /// The type of hub container: Universe, Smart, System, Mix, Playlist, Genre, Author, Collection, or Custom.
    /// Defaults to "Universe" for backward compatibility.
    /// </summary>
    public string HubType { get; set; } = "Universe";

    /// <summary>Plain-text description or rule summary for display.</summary>
    public string? Description { get; set; }

    /// <summary>Icon name hint for UI rendering (e.g. "Label", "Waves", "Person").</summary>
    public string? IconName { get; set; }

    /// <summary>"library" for library-scoped hubs, "user" for per-profile hubs.</summary>
    public string Scope { get; set; } = "library";

    /// <summary>Owner profile. Null = library-scoped (shared).</summary>
    public Guid? ProfileId { get; set; }

    /// <summary>Whether this hub is visible in browsing. Default true.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Whether this hub is pinned to media lane pages.</summary>
    public bool IsFeatured { get; set; }

    /// <summary>Minimum item count for smart hub generation threshold.</summary>
    public int MinItems { get; set; }

    /// <summary>JSON rule definition for smart hubs (e.g. genre filter, vibe filter).</summary>
    public string? RuleJson { get; set; }

    /// <summary>How items are resolved: "query" (evaluate rules at display time) or "materialized" (pre-assigned).</summary>
    public string Resolution { get; set; } = "query";

    /// <summary>SHA-256 hash of normalized RuleJson for deduplication.</summary>
    public string? RuleHash { get; set; }

    /// <summary>For content groups: field to group results by (e.g. "season", "album").</summary>
    public string? GroupByField { get; set; }

    /// <summary>Rule match mode: "all" (AND) or "any" (OR).</summary>
    public string MatchMode { get; set; } = "all";

    /// <summary>Default sort field for hub results.</summary>
    public string? SortField { get; set; }

    /// <summary>Sort direction: "asc" or "desc".</summary>
    public string SortDirection { get; set; } = "desc";

    /// <summary>Whether query-resolved results auto-refresh when library changes.</summary>
    public bool LiveUpdating { get; set; } = true;

    /// <summary>Cron expression or descriptive schedule for mix refresh.</summary>
    public string? RefreshSchedule { get; set; }

    /// <summary>When the hub's contents were last refreshed/regenerated.</summary>
    public DateTimeOffset? LastRefreshedAt { get; set; }

    /// <summary>Last modification timestamp.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    // -------------------------------------------------------------------------
    // Children
    // -------------------------------------------------------------------------

    /// <summary>
    /// All Works that belong to this Hub.
    /// This is the aggregate boundary: changes to a Hub and its Works
    /// MUST occur within a single transaction.
    /// </summary>
    public List<Work> Works { get; set; } = [];

    /// <summary>
    /// Multi-dimensional grouping signals from Wikidata that define this Hub.
    /// Each relationship links to a Wikidata QID (franchise, series, universe, etc.).
    /// </summary>
    public List<HubRelationship> Relationships { get; set; } = [];

    /// <summary>
    /// Child Hubs that belong to this Hub as a franchise/universe parent.
    /// Only populated when this Hub acts as a Parent Hub.
    /// Not loaded by default — requires explicit query.
    /// </summary>
    public List<Hub> ChildHubs { get; set; } = [];
}
