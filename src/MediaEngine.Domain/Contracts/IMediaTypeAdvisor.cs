using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Domain.Contracts;

/// <summary>
/// AI-powered media type classification. Replaces heuristic disambiguation
/// in AudioProcessor and VideoProcessor.
/// </summary>
public interface IMediaTypeAdvisor
{
    /// <summary>
    /// Classify a file's media type from its metadata signals.
    /// Returns a single classification with confidence.
    /// </summary>
    Task<MediaTypeCandidate> ClassifyAsync(
        string filename,
        string? container,
        double? durationSeconds,
        int? bitrate,
        string? genre,
        bool hasChapters,
        string? folderPath,
        CancellationToken ct = default);
}
