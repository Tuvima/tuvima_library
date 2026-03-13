namespace MediaEngine.Providers.Models;

/// <summary>
/// Describes a single Wikidata property and how it maps to a claim key.
///
/// Each entry in the <see cref="WikidataSparqlPropertyMap"/> carries one of these.
/// The property map is the single source of truth for which Wikidata P-codes
/// the Engine tracks, at what confidence, and whether the property represents
/// an external bridge identifier or a descriptive metadata field.
///
/// Defaults are compiled into <see cref="WikidataSparqlPropertyMap.DefaultMap"/>.
/// Per-instance overrides live in the universe config (<c>config/universe/wikidata.json</c>).
/// </summary>
public sealed record WikidataProperty
{
    /// <summary>
    /// The Wikidata property code, e.g. <c>"P179"</c> (Part of the series).
    /// </summary>
    public required string PCode { get; init; }

    /// <summary>
    /// The claim key this property maps to, e.g. <c>"series"</c>.
    /// Written as <c>claim_key</c> in <c>metadata_claims</c> and <c>key</c> in <c>canonical_values</c>.
    /// </summary>
    public required string ClaimKey { get; init; }

    /// <summary>
    /// Human-readable category for grouping in the Dashboard, e.g. <c>"Core Identity"</c>.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Which entity type this property applies to.
    /// <c>"Work"</c> = only fetched during work hydration.
    /// <c>"Person"</c> = only fetched during person hydration.
    /// <c>"Both"</c> = fetched in both contexts.
    /// </summary>
    public string EntityScope { get; init; } = "Work";

    /// <summary>
    /// The default confidence assigned to claims produced from this property.
    /// Bridge identifiers typically carry <c>1.0</c>; descriptive fields range from <c>0.8</c> to <c>0.9</c>.
    /// </summary>
    public double Confidence { get; init; } = 0.9;

    /// <summary>
    /// When <c>true</c>, the claim key represents an external identifier that links
    /// to another provider (e.g. TMDB ID, Apple Books ID, ASIN).
    /// Bridge properties are written to the <c>&lt;bridges&gt;</c> section of library.xml.
    /// </summary>
    public bool IsBridge { get; init; }

    /// <summary>
    /// When <c>false</c>, this property is excluded from SPARQL queries.
    /// Allows users to disable specific properties via configuration overrides
    /// without removing them from the default map.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, the Wikidata value for this property is a Q-item (entity)
    /// that requires <c>rdfs:label</c> fetching to resolve a human-readable name.
    /// Used by the SPARQL query builder to emit label variables and FILTER clauses.
    /// </summary>
    public bool IsEntityValued { get; init; }

    /// <summary>
    /// Name of the value transform to apply after extracting the raw SPARQL value.
    /// <c>null</c> means no transform — pass the raw value through.
    /// Valid names: <c>"year_from_iso"</c>, <c>"numeric_portion"</c>,
    /// <c>"strip_entity_uri"</c>, <c>"commons_url"</c>.
    /// Transform functions live in <see cref="ValueTransformRegistry"/>;
    /// which property uses which transform is data, not code.
    /// </summary>
    public string? ValueTransform { get; init; }

    /// <summary>
    /// When <c>true</c>, the SPARQL query uses <c>GROUP_CONCAT</c> to collect
    /// all values for this property. The adapter splits the result on <c>"|||"</c>
    /// and emits one <see cref="Domain.Models.ProviderClaim"/> per value.
    /// Multi-valued properties include genre, characters, cast_member, narrative_location, etc.
    /// </summary>
    public bool IsMultiValued { get; init; }

    /// <summary>
    /// When <c>true</c>, this property holds monolingual text (language-tagged literals)
    /// on Wikidata. The SPARQL query builder adds a <c>FILTER(LANG(...))</c> clause
    /// to select only the preferred language. Falls back to any language if the
    /// preferred language is unavailable.
    /// Examples: P1476 (title), P1813 (short name).
    /// </summary>
    public bool IsMonolingualText { get; init; }
}
