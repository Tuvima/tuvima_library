using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// Exports a managed subtitle beside owned media only when the configured source
/// explicitly permits metadata write-back.
/// </summary>
public interface ITextTrackExportService
{
    Task<string?> ExportPreferredSubtitleAsync(
        MediaAsset asset,
        TextTrack track,
        CancellationToken ct = default);
}
