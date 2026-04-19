using System.Text.Json.Serialization;

namespace MediaEngine.Web.Models.ViewDTOs;

public sealed class PlaybackTechnicalSummaryViewModel
{
    [JsonPropertyName("video_resolution_label")]
    public string? VideoResolutionLabel { get; set; }

    [JsonPropertyName("video_codec")]
    public string? VideoCodec { get; set; }

    [JsonPropertyName("audio_language")]
    public string? AudioLanguage { get; set; }

    [JsonPropertyName("audio_codec")]
    public string? AudioCodec { get; set; }

    [JsonPropertyName("audio_channels")]
    public string? AudioChannels { get; set; }

    [JsonPropertyName("subtitle_summary")]
    public string? SubtitleSummary { get; set; }

    [JsonPropertyName("audio_languages")]
    public List<string> AudioLanguages { get; set; } = [];

    [JsonPropertyName("subtitle_languages")]
    public List<string> SubtitleLanguages { get; set; } = [];
}

public static class PlaybackTechnicalSummaryDisplay
{
    public static string? VideoChip(PlaybackTechnicalSummaryViewModel? summary)
    {
        if (summary is null)
            return null;

        if (string.IsNullOrWhiteSpace(summary.VideoResolutionLabel))
            return summary.VideoCodec;

        return string.IsNullOrWhiteSpace(summary.VideoCodec)
            ? summary.VideoResolutionLabel
            : $"{summary.VideoResolutionLabel} ({summary.VideoCodec})";
    }

    public static string? AudioChip(PlaybackTechnicalSummaryViewModel? summary)
    {
        if (summary is null)
            return null;

        var label = string.IsNullOrWhiteSpace(summary.AudioLanguage)
            ? summary.AudioCodec
            : summary.AudioLanguage;

        if (string.IsNullOrWhiteSpace(label))
            return null;

        var suffixParts = new[] { summary.AudioCodec, summary.AudioChannels }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (suffixParts.Count == 0 || string.Equals(label, summary.AudioCodec, StringComparison.OrdinalIgnoreCase))
            return label;

        return $"{label} ({string.Join(" ", suffixParts)})";
    }

    public static string? SubtitleChip(PlaybackTechnicalSummaryViewModel? summary) =>
        summary?.SubtitleSummary;
}
