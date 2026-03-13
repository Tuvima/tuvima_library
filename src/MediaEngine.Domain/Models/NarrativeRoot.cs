namespace MediaEngine.Domain.Models;

/// <summary>
/// The narrative root for a group of related works — the broadest fictional
/// universe, franchise, or series that binds them together.
///
/// Determined by the <see cref="Services.NarrativeRootResolver"/> after Stage 1
/// hydration using the priority chain: P1434 (fictional_universe) → P8345
/// (franchise) → P179 (series) → Hub DisplayName (standalone fallback).
///
/// Maps 1:1 to a row in the <c>narrative_roots</c> table.
/// </summary>
public sealed class NarrativeRoot
{
    /// <summary>
    /// Wikidata QID of this narrative root.
    /// Example: <c>"Q3041974"</c> (Dune universe).
    /// For standalone works without a Wikidata grouping, this is the work's own QID
    /// or a generated identifier derived from the Hub DisplayName.
    /// </summary>
    public string Qid { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable label.
    /// Example: <c>"Dune universe"</c>, <c>"The Expanse"</c>.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Hierarchy level: <c>"Universe"</c>, <c>"Franchise"</c>, <c>"Series"</c>,
    /// or <c>"Standalone"</c>. See <see cref="Enums.NarrativeLevel"/>.
    /// </summary>
    public string Level { get; set; } = string.Empty;

    /// <summary>
    /// QID of the parent narrative root, if this is a franchise within a universe
    /// or a series within a franchise. Null for top-level roots.
    /// </summary>
    public string? ParentQid { get; set; }

    /// <summary>When this narrative root was first discovered.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
