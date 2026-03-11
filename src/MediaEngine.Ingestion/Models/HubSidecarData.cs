namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Data written to (and read from) the Hub-level <c>library.xml</c> sidecar.
/// The Hub-level sidecar lives at <c>{LibraryRoot}/{Category}/{HubName} ({Year})/library.xml</c>.
/// It records the identity of the creative work so the library can be reconstructed
/// from the filesystem alone (Great Inhale).
/// </summary>
public sealed class HubSidecarData
{
    /// <summary>Human-readable Hub name — typically the work's title claim.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Publication year as a four-digit string, e.g. "1937". Null if unknown.</summary>
    public string? Year { get; init; }

    /// <summary>
    /// Wikidata Q-identifier for the creative work, e.g. "Q74287".
    /// Populated by the WikidataAdapter once enrichment completes.
    /// Null until then.
    /// </summary>
    public string? WikidataQid { get; init; }

    /// <summary>
    /// Franchise or series name, e.g. "Tolkien Legendarium".
    /// Null if the work is a standalone title.
    /// </summary>
    public string? Franchise { get; init; }

    /// <summary>
    /// Wikidata coverage level: Rich, Limited, None, or Unknown.
    /// Enables filtering and scheduled refresh of items without Wikidata coverage.
    /// </summary>
    public string UniverseStatus { get; init; } = "Unknown";

    /// <summary>
    /// External bridge identifiers harvested from Wikidata SPARQL.
    /// Keys are claim keys (e.g. "tmdb_id", "imdb_id", "goodreads_id").
    /// Values are the corresponding external identifiers.
    /// Written to the <c>&lt;bridges&gt;</c> section of the Hub-level library.xml.
    /// Empty dictionary when no bridge IDs are available.
    /// </summary>
    public IReadOnlyDictionary<string, string> Bridges { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Complete snapshot of all canonical key-value pairs for the hub at the time
    /// the sidecar was last written.  Captures Wikidata-enriched fields (genre,
    /// characters, narrative_location, series, franchise, etc.) so that a Great Inhale
    /// can restore full metadata without re-fetching from providers.
    /// </summary>
    public IReadOnlyDictionary<string, string> CanonicalValues { get; init; }
        = new Dictionary<string, string>();

    /// <summary>UTC timestamp of the last organization pass that wrote this file.</summary>
    public DateTimeOffset LastOrganized { get; init; }
}
