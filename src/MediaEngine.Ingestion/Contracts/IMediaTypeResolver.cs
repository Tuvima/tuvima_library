using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Ingestion.Models;
using MediaEngine.Processors.Models;

namespace MediaEngine.Ingestion.Contracts;

/// <summary>
/// Resolves the media type that ingestion should persist for a processed file.
/// </summary>
public interface IMediaTypeResolver
{
    Task<MediaTypeResolution> ResolveAsync(
        string filePath,
        ProcessorResult processorResult,
        double categoryConfidencePrior,
        CancellationToken ct = default);
}

public sealed record MediaTypeResolution(
    MediaType MediaType,
    bool IsConflicted,
    bool NeedsReview,
    double CategoryConfidencePrior,
    IReadOnlyList<MediaTypeCandidate> Candidates,
    bool RootWatchFolderReview,
    string? RootWatchFolderDetail);
