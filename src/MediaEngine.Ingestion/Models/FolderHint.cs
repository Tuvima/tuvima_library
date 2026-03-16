namespace MediaEngine.Ingestion.Models;

/// <summary>
/// Cached metadata from the first successfully hydrated file in a source folder.
/// Used as a "hint prior" for sibling files to skip redundant Stage 1 lookups.
/// Stored in-memory only — not persisted to the database.
/// </summary>
public sealed record FolderHint
{
    /// <summary>The Hub that the first file was assigned to.</summary>
    public required Guid HubId { get; init; }

    /// <summary>Wikidata QID resolved during Stage 1 (e.g. "Q190159" for Dune).</summary>
    public string? QualifiedIdentityId { get; init; }

    /// <summary>Series name from canonical values (e.g. "Dune").</summary>
    public string? SeriesName { get; init; }

    /// <summary>Primary author or artist name.</summary>
    public string? AuthorOrArtist { get; init; }

    /// <summary>The title of the first file (for title-based divergence detection).</summary>
    public string? Title { get; init; }

    /// <summary>
    /// Bridge identifiers deposited by Stage 1 (e.g. isbn → "978-...", tmdb_id → "438631").
    /// Keys are claim keys matching the property map (isbn, asin, tmdb_movie_id, etc.).
    /// </summary>
    public Dictionary<string, string> BridgeIds { get; init; } = new();

    /// <summary>When this hint was created (UTC). Expires after 24 hours.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;

    /// <summary>The source folder path this hint applies to.</summary>
    public required string SourceFolderPath { get; init; }

    /// <summary>The media type of the first file (for divergence detection).</summary>
    public string? MediaTypeCategory { get; init; }

    /// <summary>Check if this hint has expired (older than 24 hours).</summary>
    public bool IsExpired => DateTime.UtcNow - CreatedAtUtc > TimeSpan.FromHours(24);
}
