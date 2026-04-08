namespace MediaEngine.Domain.Enums;

/// <summary>
/// Indicates whether a Work is physically present in the library (Owned)
/// or is known externally (via Wikidata or a retail provider) but has no
/// corresponding file yet (Unowned / catalog-only).
///
/// This mirrors the <c>is_catalog_only</c> column on the <c>works</c> table
/// (and is kept in sync with it by migration M-083) but is surfaced as a
/// typed enum for use in the API and UI layers.
/// </summary>
public enum OwnershipStatus
{
    /// <summary>The library contains at least one file for this Work.</summary>
    Owned = 0,

    /// <summary>
    /// No file exists yet. This Work was discovered from Wikidata child
    /// entity data (<c>child_entities_json</c>) or a retail provider.
    /// When a matching file is ingested, the row is promoted to
    /// <see cref="Owned"/> and <c>work_kind</c> is updated from
    /// <c>catalog</c> to <c>child</c>.
    /// </summary>
    Unowned = 1,
}
