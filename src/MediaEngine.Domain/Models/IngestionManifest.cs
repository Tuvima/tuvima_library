namespace MediaEngine.Domain.Models;

/// <summary>
/// Structured output from the LLM's batch analysis of incoming files.
/// Groups files by series/album/work and specifies targeted retail API queries.
/// </summary>
public sealed class IngestionManifest
{
    /// <summary>Grouped file analysis results.</summary>
    public required IReadOnlyList<ManifestGroup> Groups { get; init; }

    /// <summary>LLM processing time in milliseconds.</summary>
    public long ProcessingTimeMs { get; init; }
}

/// <summary>
/// A group of related files identified by the LLM (e.g. a TV season, an album, a book series).
/// </summary>
public sealed class ManifestGroup
{
    /// <summary>What kind of group this is (e.g. "tv_season", "album", "book_series", "single_work").</summary>
    public required string GroupType { get; init; }

    /// <summary>The inferred media type for this group.</summary>
    public required Enums.MediaType MediaType { get; init; }

    /// <summary>LLM confidence in the group classification (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Clean series/album/collection title.</summary>
    public string? SeriesTitle { get; init; }

    /// <summary>Release year, if identified.</summary>
    public int? Year { get; init; }

    /// <summary>Season number for TV groups.</summary>
    public int? Season { get; init; }

    /// <summary>Artist/author name, if identified.</summary>
    public string? Creator { get; init; }

    /// <summary>Hard identifier found in file metadata (ISBN, ASIN), if any.</summary>
    public string? HardIdentifier { get; init; }

    /// <summary>Type of hard identifier ("isbn", "asin", "tmdb_id", etc.).</summary>
    public string? HardIdentifierType { get; init; }

    /// <summary>Recommended retail provider to query (reads from slot config).</summary>
    public string? RetailProvider { get; init; }

    /// <summary>Recommended search query for the retail provider.</summary>
    public string? RetailQuery { get; init; }

    /// <summary>Individual files in this group with per-file metadata.</summary>
    public required IReadOnlyList<ManifestFile> Files { get; init; }
}

/// <summary>
/// Per-file metadata within a manifest group.
/// </summary>
public sealed class ManifestFile
{
    /// <summary>Full path to the file on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Clean title for this specific file.</summary>
    public required string Title { get; init; }

    /// <summary>LLM confidence in this file's metadata (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Episode number within the group (TV, podcast).</summary>
    public int? EpisodeNumber { get; init; }

    /// <summary>Track number within the group (music albums).</summary>
    public int? TrackNumber { get; init; }

    /// <summary>Episode/track title distinct from the series title.</summary>
    public string? EpisodeTitle { get; init; }
}
