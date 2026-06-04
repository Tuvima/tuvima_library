using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers.Models;

/// <summary>
/// Unified Stage 2 resolution request consumed by
/// <c>ReconciliationAdapter.ResolveAsync</c> and <c>ResolveBatchAsync</c>.
///
/// <para>
/// A single request shape covers all three resolution strategies:
/// bridge ID lookup and music album resolution.
/// The adapter dispatches based on <see cref="Strategy"/> (or auto-detects when
/// <see cref="ResolveStrategy.Auto"/> is set).
/// </para>
///
/// <para>
/// In batch mode, the adapter groups requests by their natural key — music
/// requests by <c>album|artist</c>, bridge requests by their primary bridge
/// ID, and text requests by <c>title|author|mediaType</c> — so N requests
/// produce far fewer than N Wikidata calls. The <see cref="CorrelationKey"/>
/// is what the caller uses to look up its own result in the returned dictionary.
/// </para>
/// </summary>
public sealed class WikidataResolveRequest
{
    /// <summary>
    /// Caller-supplied opaque key used to address this request's result in
    /// the dictionary returned by <c>ResolveBatchAsync</c>. Typically the
    /// identity job id or the entity id. Must be unique within a batch.
    /// </summary>
    public required string CorrelationKey { get; init; }

    /// <summary>The media type — drives type filtering and strategy auto-detection.</summary>
    public required MediaType MediaType { get; init; }

    /// <summary>
    /// Resolution strategy. Leave as <see cref="ResolveStrategy.Auto"/> to let
    /// the adapter pick the best strategy from the populated fields.
    /// </summary>
    public ResolveStrategy Strategy { get; init; } = ResolveStrategy.Auto;

    // ── Bridge ID strategy ───────────────────────────────────────────────────

    /// <summary>
    /// Bridge IDs collected during Stage 1 (e.g. <c>tmdb_id</c>, <c>isbn</c>, <c>asin</c>).
    /// Non-music Stage 2 resolution requires at least one real bridge ID.
    /// </summary>
    public IReadOnlyDictionary<string, string>? BridgeIds { get; init; }

    /// <summary>
    /// Mapping from bridge ID type → Wikidata property code (e.g. <c>tmdb_id → P4947</c>).
    /// Required when <see cref="BridgeIds"/> is set.
    /// </summary>
    public IReadOnlyDictionary<string, string>? WikidataProperties { get; init; }

    /// <summary>
    /// When true, the resolved entity is checked for P629 to determine whether
    /// it is an edition (the work QID is then surfaced via <c>WorkQid</c>).
    /// Set for Books, Audiobooks, and Music.
    /// </summary>
    public bool IsEditionAware { get; init; }

    // ── Music album strategy ─────────────────────────────────────────────────

    /// <summary>The album title (used by <see cref="ResolveStrategy.MusicAlbum"/>).</summary>
    public string? AlbumTitle { get; init; }

    /// <summary>The album artist (used by <see cref="ResolveStrategy.MusicAlbum"/>).</summary>
    public string? Artist { get; init; }

    // ── Text reconciliation strategy ────────────────────────────────────────

    /// <summary>The work title.</summary>
    public string? Title { get; init; }

    /// <summary>The author.</summary>
    public string? Author { get; init; }

    /// <summary>The release/publication year used to reject bad text-only drift.</summary>
    public string? Year { get; init; }

    /// <summary>
    /// BCP-47 language detected from the source file. When it differs from the
    /// configured metadata language, Wikidata label/description lookups prefer
    /// this language and fall back to the configured metadata language.
    /// </summary>
    public string? FileLanguage { get; init; }

    /// <summary>
    /// Optional parent/container title for fallback roll-up resolution.
    /// Currently used for comics to retry against the series title when the
    /// issue-level entity is missing on Wikidata.
    /// </summary>
    public string? SeriesTitle { get; init; }

    /// <summary>Optional TV season hint used by bridge scoring.</summary>
    public int? SeasonNumber { get; init; }

    /// <summary>Optional TV episode hint used by bridge scoring.</summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>Optional comic issue hint used by bridge scoring.</summary>
    public string? IssueNumber { get; init; }
}
