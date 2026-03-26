namespace MediaEngine.Domain.Models;

/// <summary>
/// Result of AI-powered filename cleaning (Smart Labeling).
/// Replaces the regex-based TitleNormalizer output.
/// </summary>
public sealed class CleanedSearchQuery
{
    /// <summary>The cleaned, human-readable title.</summary>
    public required string Title { get; init; }

    /// <summary>Author name extracted from filename, if present.</summary>
    public string? Author { get; init; }

    /// <summary>Release year extracted from filename, if present (1800-2100).</summary>
    public int? Year { get; init; }

    /// <summary>TV season number, if detected.</summary>
    public int? Season { get; init; }

    /// <summary>TV episode number, if detected.</summary>
    public int? Episode { get; init; }

    /// <summary>LLM confidence in the extraction (0.0-1.0).</summary>
    public double Confidence { get; init; }
}
