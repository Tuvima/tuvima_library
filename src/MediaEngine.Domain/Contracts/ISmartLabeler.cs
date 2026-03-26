using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered filename cleaning. Replaces TitleNormalizer.
/// Extracts clean title, author, year from messy filenames.
/// </summary>
public interface ISmartLabeler
{
    /// <summary>
    /// Clean a raw filename into structured metadata.
    /// Uses the text_quality model for batch ingestion.
    /// </summary>
    Task<CleanedSearchQuery> CleanAsync(string rawFilename, CancellationToken ct = default);
}
