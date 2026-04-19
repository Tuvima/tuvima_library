using System.Text.Json.Serialization;

namespace MediaEngine.Domain.Models;

/// <summary>
/// Shared technical playback summary used by watch detail and player surfaces.
/// Intended to be stable across TV and movie pages.
/// </summary>
public sealed record PlaybackTechnicalSummary
{
    [JsonPropertyName("video_resolution_label")]
    public string? VideoResolutionLabel { get; init; }

    [JsonPropertyName("video_codec")]
    public string? VideoCodec { get; init; }

    [JsonPropertyName("audio_language")]
    public string? AudioLanguage { get; init; }

    [JsonPropertyName("audio_codec")]
    public string? AudioCodec { get; init; }

    [JsonPropertyName("audio_channels")]
    public string? AudioChannels { get; init; }

    [JsonPropertyName("subtitle_summary")]
    public string? SubtitleSummary { get; init; }

    [JsonPropertyName("audio_languages")]
    public IReadOnlyList<string> AudioLanguages { get; init; } = [];

    [JsonPropertyName("subtitle_languages")]
    public IReadOnlyList<string> SubtitleLanguages { get; init; } = [];
}
