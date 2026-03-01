using Tanaste.Domain.Enums;

namespace Tanaste.Providers.Models;

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

    /// <summary>Apple Books ID (Wikidata P3861). Used for QID cross-reference.</summary>
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

    // ── Infrastructure ────────────────────────────────────────────────────────

    /// <summary>
    /// The resolved base URL for the adapter's API, read from
    /// <c>TanasteMasterManifest.ProviderEndpoints</c>.
    /// Adapters must never hard-code URLs; this field is always populated by
    /// the harvesting service before the request is dispatched.
    /// </summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>
    /// The resolved SPARQL endpoint URL for Wikidata, read from
    /// <c>TanasteMasterManifest.ProviderEndpoints["wikidata_sparql"]</c>.
    /// Used by the <c>WikidataAdapter</c> for deep-hydration SPARQL queries.
    /// Null for non-Wikidata providers.
    /// </summary>
    public string? SparqlBaseUrl { get; init; }
}
