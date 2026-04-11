using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// Configuration for media type disambiguation during file ingestion.
///
/// Controls the confidence thresholds that determine whether a file's media type
/// is auto-assigned, flagged for review, or left as unknown.
/// Also carries configurable heuristic parameters for audio and video analysis.
///
/// Loaded from <c>config/disambiguation.json</c>.
/// </summary>
public sealed class DisambiguationSettings
{
    /// <summary>
    /// Minimum confidence for auto-assigning a media type without review.
    /// Files above this threshold are classified automatically.
    /// Default: 0.70.
    /// </summary>
    [JsonPropertyName("media_type_auto_assign_threshold")]
    public double MediaTypeAutoAssignThreshold { get; set; } = 0.70;

    /// <summary>
    /// Minimum confidence for creating a review queue entry.
    /// Files between this and <see cref="MediaTypeAutoAssignThreshold"/> are
    /// provisionally classified but flagged for user review.
    /// Files below this threshold are assigned <c>MediaType.Unknown</c>.
    /// Default: 0.40.
    /// </summary>
    [JsonPropertyName("media_type_review_threshold")]
    public double MediaTypeReviewThreshold { get; set; } = 0.40;

    /// <summary>Audio-specific heuristic parameters.</summary>
    [JsonPropertyName("audio_heuristics")]
    public AudioHeuristicSettings AudioHeuristics { get; set; } = new();

    /// <summary>Video-specific heuristic parameters.</summary>
    [JsonPropertyName("video_heuristics")]
    public VideoHeuristicSettings VideoHeuristics { get; set; } = new();
}

/// <summary>Configurable parameters for audio media type disambiguation.</summary>
public sealed class AudioHeuristicSettings
{
    [JsonPropertyName("duration_long_minutes")]
    public int DurationLongMinutes { get; set; } = 60;

    [JsonPropertyName("duration_short_minutes")]
    public int DurationShortMinutes { get; set; } = 7;

    [JsonPropertyName("low_bitrate_kbps")]
    public int LowBitrateKbps { get; set; } = 96;

    [JsonPropertyName("high_bitrate_kbps")]
    public int HighBitrateKbps { get; set; } = 192;

    [JsonPropertyName("large_file_mb")]
    public int LargeFileMb { get; set; } = 100;

    [JsonPropertyName("path_keywords_audiobook")]
    public List<string> PathKeywordsAudiobook { get; set; } = ["audiobook", "audiobooks", "narrated"];

    [JsonPropertyName("path_keywords_music")]
    public List<string> PathKeywordsMusic { get; set; } = ["music", "songs", "albums", "tracks"];

    [JsonPropertyName("genre_audiobook")]
    public List<string> GenreAudiobook { get; set; } = ["audiobook", "speech", "spoken word"];
}

/// <summary>Configurable parameters for video media type disambiguation.</summary>
public sealed class VideoHeuristicSettings
{
    [JsonPropertyName("tv_filename_patterns")]
    public List<string> TvFilenamePatterns { get; set; } = [@"S\d{2}E\d{2}", @"\d{1,2}x\d{2}"];

    [JsonPropertyName("movie_duration_minutes")]
    public int MovieDurationMinutes { get; set; } = 60;

    [JsonPropertyName("tv_duration_range_minutes")]
    public List<int> TvDurationRangeMinutes { get; set; } = [15, 45];

    [JsonPropertyName("large_file_gb")]
    public int LargeFileGb { get; set; } = 4;

    [JsonPropertyName("path_keywords_movie")]
    public List<string> PathKeywordsMovie { get; set; } = ["movie", "movies", "film", "films"];

    [JsonPropertyName("path_keywords_tv")]
    public List<string> PathKeywordsTv { get; set; } = ["season", "series", "tv", "show", "shows"];
}
