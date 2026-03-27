using MediaEngine.Domain.Enums;

namespace MediaEngine.Providers.Models;

/// <summary>
/// All context an external provider adapter needs to perform a single lookup.
///
/// Built by <c>MetadataHarvestingService</c> from a <see cref="Domain.Models.HarvestRequest"/>
/// and passed to <see cref="Contracts.IExternalMetadataProvider.FetchAsync"/>.
/// Adapters must treat every nullable field as optional and return an empty
/// result list if the required identifiers for their lookup are absent.
///
/// Spec: Phase 9 – External Metadata Adapters § Request Contract.
/// </summary>
public sealed class ProviderLookupRequest
{
    /// <summary>Domain entity being enriched.</summary>
    public Guid EntityId { get; init; }

    /// <summary>The kind of entity (MediaAsset, Person, Work, Edition).</summary>
    public EntityType EntityType { get; init; }

    /// <summary>Media type of the asset (Epub, Audiobook, Movie, …).</summary>
    public MediaType MediaType { get; init; }

    // ── Common hints ──────────────────────────────────────────────────────────

    /// <summary>Work or asset title, e.g. <c>"Dune"</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Author name, e.g. <c>"Frank Herbert"</c>.</summary>
    public string? Author { get; init; }

    /// <summary>Narrator name (audiobooks), e.g. <c>"Scott Brick"</c>.</summary>
    public string? Narrator { get; init; }

    // ── Identifier hints ──────────────────────────────────────────────────────

    /// <summary>Amazon Standard Identification Number. Required by Audnexus.</summary>
    public string? Asin { get; init; }

    /// <summary>ISBN-10 or ISBN-13. Used by Open Library and Apple Books.</summary>
    public string? Isbn { get; init; }

    // ── External bridge hints ───────────────────────────────────────────────

    /// <summary>Apple Books ID (Wikidata P6395). Used for QID cross-reference.</summary>
    public string? AppleBooksId { get; init; }

    /// <summary>Audible ID (Wikidata P3398). Used for QID cross-reference.</summary>
    public string? AudibleId { get; init; }

    /// <summary>TMDB ID (Wikidata P4947). Used for QID cross-reference.</summary>
    public string? TmdbId { get; init; }

    /// <summary>IMDb ID (Wikidata P345). Used for QID cross-reference.</summary>
    public string? ImdbId { get; init; }

    // ── Person-enrichment hints ───────────────────────────────────────────────

    /// <summary>
    /// For <see cref="EntityType.Person"/> requests: the person's display name.
    /// Used by <c>WikidataAdapter</c> to search for the Wikidata entity.
    /// </summary>
    public string? PersonName { get; init; }

    /// <summary>
    /// For <see cref="EntityType.Person"/> requests: the person's role.
    /// Values: <c>"Author"</c>, <c>"Narrator"</c>, <c>"Director"</c>.
    /// </summary>
    public string? PersonRole { get; init; }

    // ── Pipeline hints ───────────────────────────────────────────────────────

    /// <summary>
    /// When set, the Wikidata adapter skips QID resolution and goes straight
    /// to SPARQL deep-ingest using this Q-identifier. Set by the hydration
    /// pipeline when the user selects a disambiguation candidate from the
    /// review queue.
    /// </summary>
    public string? PreResolvedQid { get; init; }

    // ── Fictional entity hints ────────────────────────────────────────────────

    /// <summary>
    /// Generic key-value hints for adapter-specific context.
    /// Used by fictional entity enrichment to pass <c>"wikidata_qid"</c>
    /// when the QID is already known from the parent work's hydration.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Hints { get; init; }

    // ── Infrastructure ────────────────────────────────────────────────────────

    /// <summary>
    /// The resolved base URL for the adapter's API, read from
    /// <c>LegacyManifest.ProviderEndpoints</c>.
    /// Adapters must never hard-code URLs; this field is always populated by
    /// the harvesting service before the request is dispatched.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// The resolved SPARQL endpoint URL for Wikidata, read from
    /// <c>LegacyManifest.ProviderEndpoints["wikidata_sparql"]</c>.
    /// Used by the <c>WikidataAdapter</c> for deep-hydration SPARQL queries.
    /// Null for non-Wikidata providers.
    /// </summary>
    public string? SparqlBaseUrl { get; init; }

    /// <summary>
    /// BCP-47 two-letter language code from server regional settings (e.g. "en", "fr").
    /// Used by <c>ConfigDrivenAdapter</c> as the <c>{lang}</c> URL template variable
    /// and by <c>ReconciliationAdapter</c> for search and label language preference.
    /// </summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// BCP-47 two-letter language code detected from the file's embedded metadata
    /// (e.g. EPUB dc:language, ID3 lang tag, Whisper detection). Used by
    /// <c>ReconciliationAdapter</c> for multi-language Wikidata search when the
    /// file's language differs from the configured metadata language.
    /// Null when no file language was detected.
    /// </summary>
    public string? FileLanguage { get; init; }

    /// <summary>
    /// ISO 3166-1 alpha-2 country code, lowercased, from server regional settings (e.g. "us", "gb").
    /// Used by <c>ConfigDrivenAdapter</c> as the <c>{country}</c> URL template variable
    /// (e.g. Apple Books storefront selection).
    /// </summary>
    public string Country { get; init; } = "us";

    /// <summary>
    /// Which enrichment pass this lookup is part of.
    /// When <see cref="HydrationPass.Quick"/>, the Wikidata adapter uses a
    /// reduced SPARQL query fetching only core properties and bridge IDs.
    /// When <see cref="HydrationPass.Universe"/>, the full 50+ property
    /// query is used.
    /// </summary>
    public HydrationPass HydrationPass { get; init; } = HydrationPass.Quick;
}
