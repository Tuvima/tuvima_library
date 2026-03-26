namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Extracts structured metadata from a user-provided URL (Goodreads, IMDb, Apple Books).
/// Advanced option in the review queue.
/// </summary>
public interface IUrlMetadataExtractor
{
    /// <summary>
    /// Fetch a URL and extract structured metadata using the LLM.
    /// Returns extracted fields as key-value pairs.
    /// </summary>
    Task<UrlExtractionResult> ExtractAsync(string url, CancellationToken ct = default);
}

/// <summary>
/// Result of URL metadata extraction.
/// </summary>
public sealed class UrlExtractionResult
{
    /// <summary>Whether extraction succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Extracted metadata fields (title, author, year, isbn, description, etc.).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    /// <summary>LLM confidence in the extraction (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>Error message if extraction failed.</summary>
    public string? ErrorMessage { get; init; }
}
