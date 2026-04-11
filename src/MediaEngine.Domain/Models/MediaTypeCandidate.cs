using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Models;

/// <summary>
/// A candidate media type produced by heuristic analysis during file processing.
///
/// When a container format is ambiguous (e.g. MP3 can be Audiobook or Music),
/// the processor emits multiple candidates with varying confidence values. The ingestion
/// engine resolves the winner using the same Weighted Voter principles as metadata claims.
/// </summary>
public sealed class MediaTypeCandidate
{
    /// <summary>The candidate media type.</summary>
    public required MediaType Type { get; init; }

    /// <summary>Confidence in this classification (0.0–1.0).</summary>
    public required double Confidence { get; init; }

    /// <summary>Human-readable explanation of the heuristic signals that produced this score.</summary>
    public required string Reason { get; init; }
}
