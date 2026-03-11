using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Reads and serves EPUB content — chapters, embedded resources, TOC, and full-text search.
/// Uses an LRU memory cache to avoid re-parsing the same book on every request.
/// </summary>
public interface IEpubContentService
{
    /// <summary>
    /// Extracts high-level metadata from the EPUB at <paramref name="filePath"/>.
    /// </summary>
    Task<EpubBookMetadata> GetBookMetadataAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Builds a hierarchical Table of Contents from the EPUB navigation document.
    /// Falls back to a flat list based on reading order if no navigation is present.
    /// </summary>
    Task<List<EpubTocEntry>> GetTableOfContentsAsync(string filePath, CancellationToken ct = default);

    /// <summary>
    /// Returns the HTML content of a single chapter by reading-order index.
    /// Relative resource URLs are rewritten to point at <paramref name="resourceBaseUrl"/>.
    /// </summary>
    Task<EpubChapterContent?> GetChapterContentAsync(
        string filePath,
        int chapterIndex,
        string resourceBaseUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Extracts an embedded resource (image, CSS, font) from the EPUB by its internal path.
    /// </summary>
    Task<EpubResourceResult?> GetResourceAsync(string filePath, string resourcePath, CancellationToken ct = default);

    /// <summary>
    /// Searches all chapters for the given query string (case-insensitive substring match).
    /// Returns context snippets of approximately 50 characters around each match.
    /// </summary>
    Task<List<EpubSearchHit>> SearchAsync(string filePath, string query, CancellationToken ct = default);
}
