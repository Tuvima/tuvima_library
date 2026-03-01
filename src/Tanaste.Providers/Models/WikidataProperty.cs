namespace Tanaste.Providers.Models;

/// <summary>
/// Describes a single Wikidata property and how it maps to a Tanaste claim key.
///
/// Each entry in the <see cref="WikidataSparqlPropertyMap"/> carries one of these.
/// The property map is the single source of truth for which Wikidata P-codes
/// the Engine tracks, at what confidence, and whether the property represents
/// an external bridge identifier or a descriptive metadata field.
///
/// Defaults are compiled into <see cref="WikidataSparqlPropertyMap.DefaultMap"/>.
/// Per-instance overrides live in <c>tanaste_master.json → wikidata_property_map</c>.
/// </summary>
public sealed record WikidataProperty
{
    /// <summary>
    /// The Wikidata property code, e.g. <c>"P179"</c> (Part of the series).
    /// </summary>
    public required string PCode { get; init; }

    /// <summary>
    /// The Tanaste claim key this property maps to, e.g. <c>"series"</c>.
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
    /// Bridge properties are written to the <c>&lt;bridges&gt;</c> section of tanaste.xml.
    /// </summary>
    public bool IsBridge { get; init; }

    /// <summary>
    /// When <c>false</c>, this property is excluded from SPARQL queries.
    /// Allows users to disable specific properties via <c>tanaste_master.json</c> overrides
    /// without removing them from the default map.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
