namespace MediaEngine.Ingestion.Models;

/// <summary>
/// A single metadata field that was explicitly set by the user (is_user_locked = 1).
/// Stored in the Edition-level sidecar so user decisions survive a database wipe.
/// </summary>
public sealed class UserLockedClaim
{
    /// <summary>The metadata field key, e.g. "title".</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>The user-chosen value for this field.</summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>When the user locked this field.</summary>
    public DateTimeOffset LockedAt { get; init; }
}

/// <summary>
/// Data written to (and read from) the Edition-level <c>library.xml</c> sidecar.
/// Lives at <c>{LibraryRoot}/{Category}/{HubName} ({Year})/{Format} - {Edition}/library.xml</c>.
/// Records the identity and provenance of a single file so the library can be
/// reconstructed from the filesystem alone (Great Inhale).
/// </summary>
public sealed class EditionSidecarData
{
    /// <summary>Resolved title canonical value.</summary>
    public string? Title { get; init; }

    /// <summary>Resolved author canonical value.</summary>
    public string? Author { get; init; }

    /// <summary>Detected media type, e.g. "Epub", "Audiobook", "Movie".</summary>
    public string? MediaType { get; init; }

    /// <summary>ISBN-13 identifier. Null if absent.</summary>
    public string? Isbn { get; init; }

    /// <summary>Amazon Standard Identification Number. Null if absent.</summary>
    public string? Asin { get; init; }

    /// <summary>Wikidata QID for the work, e.g. "Q190159". Null if not yet enriched.</summary>
    public string? WikidataQid { get; init; }

    /// <summary>Wikidata QID for the title entity (same as WikidataQid for most works).</summary>
    public string? TitleQid { get; init; }

    /// <summary>Wikidata QID for the author entity, e.g. "Q44118".</summary>
    public string? AuthorQid { get; init; }

    /// <summary>SHA-256 hex content hash — the file's permanent identity.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>
    /// Relative path (from the edition folder) to the cover image.
    /// Always "cover.jpg" when written by the Engine.
    /// </summary>
    public string CoverPath { get; init; } = "cover.jpg";

    /// <summary>User-locked metadata claims to preserve across DB rebuilds.</summary>
    public IReadOnlyList<UserLockedClaim> UserLocks { get; init; } = [];

    /// <summary>
    /// Complete snapshot of all canonical key-value pairs at the time the sidecar was last written.
    /// Includes all enriched fields (genre, characters, series, narrator, description, etc.)
    /// so that a Great Inhale can restore full metadata without re-fetching from providers.
    /// Keys that already have dedicated elements (title, author, isbn, asin) are included here
    /// as well for a self-contained record.
    /// </summary>
    public IReadOnlyDictionary<string, string> CanonicalValues { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Multi-valued canonical fields decomposed into arrays with optional QIDs.
    /// Key = field name (e.g. "genre"), Value = (display values, QIDs).
    /// Semicolon-separated in the sidecar XML <c>values</c> and <c>qids</c> attributes.
    /// </summary>
    public IReadOnlyDictionary<string, MultiValuedCanonical> MultiValuedCanonicals { get; init; }
        = new Dictionary<string, MultiValuedCanonical>();

    /// <summary>UTC timestamp of the last organization pass that wrote this file.</summary>
    public DateTimeOffset LastOrganized { get; init; }
}

/// <summary>
/// Represents a multi-valued canonical field with display values and optional QIDs.
/// </summary>
public sealed class MultiValuedCanonical
{
    /// <summary>Display label values (e.g. "Science fiction", "Space opera").</summary>
    public string[] Values { get; init; } = [];

    /// <summary>
    /// Wikidata QIDs corresponding to each value (same order).
    /// May be shorter than <see cref="Values"/> if some entries lack QIDs.
    /// </summary>
    public string[] Qids { get; init; } = [];
}
