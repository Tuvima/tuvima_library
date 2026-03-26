namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered one-sentence summary generator.
/// Condenses long Wikipedia/Wikidata descriptions into punchy TL;DRs.
/// </summary>
public interface ITldrGenerator
{
    /// <summary>
    /// Generate a single-sentence, spoiler-free summary.
    /// Uses text_fast model for on-demand responsiveness.
    /// </summary>
    Task<string?> SummarizeAsync(string longDescription, CancellationToken ct = default);
}
