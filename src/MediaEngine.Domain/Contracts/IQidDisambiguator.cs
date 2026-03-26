using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered Wikidata QID disambiguation. When Stage 2 returns multiple
/// candidates and no hard identifiers exist, the LLM picks the best match.
/// </summary>
public interface IQidDisambiguator
{
    /// <summary>
    /// Compare file metadata against Wikidata candidates and select the best match.
    /// Returns null SelectedQid if the LLM cannot decide with sufficient confidence.
    /// </summary>
    Task<DisambiguationResult> DisambiguateAsync(
        IReadOnlyDictionary<string, string> fileMetadata,
        IReadOnlyList<QidCandidate> candidates,
        CancellationToken ct = default);
}

/// <summary>
/// Result of LLM disambiguation between Wikidata candidates.
/// </summary>
public sealed class DisambiguationResult
{
    /// <summary>The QID selected by the LLM, or null if it could not decide.</summary>
    public string? SelectedQid { get; init; }

    /// <summary>LLM confidence in the selection (0.0-1.0).</summary>
    public double Confidence { get; init; }

    /// <summary>LLM's reasoning for the selection.</summary>
    public string? Reasoning { get; init; }
}
