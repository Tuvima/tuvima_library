namespace MediaEngine.Providers.Models;

/// <summary>
/// Identifies which resolution strategy was used (or should be used) by
/// <c>ReconciliationAdapter.ResolveAsync</c> / <c>ResolveBatchAsync</c>.
/// </summary>
public enum ResolveStrategy
{
    /// <summary>
    /// No strategy specified — the adapter will auto-detect based on the request shape:
    /// music + album → <see cref="MusicAlbum"/>; non-music with bridge IDs → <see cref="BridgeId"/>;
    /// otherwise no automatic match is produced.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Bridge ID lookup via Wikidata <c>haswbstatement</c> CirrusSearch.
    /// Does not fall back to title-only reconciliation when no bridge ID resolves.
    /// </summary>
    BridgeId = 1,

    /// <summary>
    /// Music album resolution using the dedicated MusicAlbum class list (Q482994 et al.).
    /// Targets the album rather than the individual track.
    /// </summary>
    MusicAlbum = 2,

    /// <summary>The request did not produce a match.</summary>
    NotResolved = 3,

    /// <summary>
    /// Constrained title/creator fallback used only after a trusted retail match.
    /// </summary>
    TextSearch = 4,
}
