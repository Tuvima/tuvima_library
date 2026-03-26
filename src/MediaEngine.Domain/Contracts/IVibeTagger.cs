namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered mood and vibe tagging from Wikipedia summaries.
/// Tags are drawn from per-category controlled vocabularies.
/// </summary>
public interface IVibeTagger
{
    /// <summary>
    /// Generate vibe tags for a work using its Wikipedia summary and genre information.
    /// Returns tags from the controlled vocabulary for the work's media category.
    /// </summary>
    Task<IReadOnlyList<string>> TagAsync(
        string title,
        string? wikipediaSummary,
        IReadOnlyList<string> genres,
        string mediaCategory,
        CancellationToken ct = default);
}
