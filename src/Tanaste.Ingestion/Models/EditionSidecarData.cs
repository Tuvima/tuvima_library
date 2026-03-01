namespace Tanaste.Ingestion.Models;

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
/// Data written to (and read from) the Edition-level <c>tanaste.xml</c> sidecar.
/// Lives at <c>{LibraryRoot}/{Category}/{HubName} ({Year})/{Format} - {Edition}/tanaste.xml</c>.
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
    /// External bridge identifiers harvested from Wikidata SPARQL.
    /// Keys are Tanaste claim keys (e.g. "apple_books_id", "asin", "audible_id").
    /// Values are the corresponding external identifiers.
    /// Written to the <c>&lt;bridges&gt;</c> section of the Edition-level tanaste.xml.
    /// Empty dictionary when no bridge IDs are available.
    /// </summary>
    public IReadOnlyDictionary<string, string> Bridges { get; init; }
        = new Dictionary<string, string>();

    /// <summary>UTC timestamp of the last organization pass that wrote this file.</summary>
    public DateTimeOffset LastOrganized { get; init; }
}
