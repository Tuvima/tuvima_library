using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;

namespace MediaEngine.Domain.Contracts;

public interface ITextTrackProvider
{
    string Name { get; }

    TextTrackKind Kind { get; }

    bool IsEnabled { get; }

    bool CanHandle(MediaType mediaType);

    Task<IReadOnlyList<TextTrackCandidate>> SearchAsync(TextTrackLookup lookup, CancellationToken ct = default);

    Task<TextTrackDownload?> DownloadAsync(TextTrackCandidate candidate, CancellationToken ct = default);
}

public sealed record TextTrackLookup(
    MediaAsset Asset,
    MediaType MediaType,
    string? Title,
    string? Artist,
    string? Album,
    string? Year,
    string? Language,
    double? DurationSeconds,
    IReadOnlyDictionary<string, string> BridgeIds);

public sealed record TextTrackCandidate(
    string Provider,
    TextTrackKind Kind,
    string SourceId,
    string? SourceUrl,
    string Language,
    string SourceFormat,
    double Confidence,
    bool IsHearingImpaired,
    double? DurationMatchScore,
    object? Payload = null);

public sealed record TextTrackDownload(
    TextTrackCandidate Candidate,
    string Content,
    string SourceFormat,
    string NormalizedFormat);
