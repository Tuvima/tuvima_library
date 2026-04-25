using System.Text.Json.Serialization;

namespace MediaEngine.Storage.Models;

/// <summary>
/// FFmpeg and transcoding configuration loaded from <c>config/transcoding.json</c>.
/// All paths default to empty string, which triggers auto-detection.
/// Hardware acceleration defaults to "auto" — the service probes for NVENC,
/// QuickSync, and VAAPI and selects the best available option.
/// </summary>
public sealed class TranscodingSettings
{
    /// <summary>
    /// Explicit path to ffmpeg.exe / ffmpeg binary.
    /// Leave empty to enable auto-detection (tools/ffmpeg/ → PATH).
    /// </summary>
    [JsonPropertyName("ffmpeg_binary_path")]
    public string FfmpegBinaryPath  { get; set; } = string.Empty;

    /// <summary>
    /// Explicit path to ffprobe.exe / ffprobe binary.
    /// Leave empty to enable auto-detection (tools/ffmpeg/ → PATH).
    /// </summary>
    [JsonPropertyName("ffprobe_binary_path")]
    public string FfprobeBinaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Hardware acceleration mode: "auto" | "nvenc" | "quicksync" | "vaapi" | "none".
    /// "auto" probes available encoders and selects the best option with software fallback.
    /// </summary>
    [JsonPropertyName("hardware_acceleration")]
    public string HardwareAcceleration { get; set; } = "auto";

    /// <summary>Maximum number of concurrent transcoding jobs.</summary>
    [JsonPropertyName("max_concurrent_transcodes")]
    public int MaxConcurrentTranscodes { get; set; } = 1;

    /// <summary>Maximum disk space (GB) for shadow transcoded copies.</summary>
    [JsonPropertyName("shadow_storage_limit_gb")]
    public int ShadowStorageLimitGb { get; set; } = 500;

    /// <summary>Quality profiles available for transcoding.</summary>
    [JsonPropertyName("quality_profiles")]
    public List<TranscodingQualityProfile> QualityProfiles { get; set; } =
    [
        new() { Name = "mobile-small", Resolution = "540p", Codec = "h264", AudioCodec = "aac", Container = "mp4", Bitrate = "1M", SizeGuidance = "400-700 MB for a typical 2-hour movie" },
        new() { Name = "mobile-standard", Resolution = "720p", Codec = "h264", AudioCodec = "aac", Container = "mp4", Bitrate = "2M", SizeGuidance = "Offline-friendly quality" },
        new() { Name = "tv-direct-hls", Resolution = "source", Codec = "h264", AudioCodec = "aac", Container = "hls", Bitrate = "source", SizeGuidance = "High-quality TV fallback" },
        new() { Name = "audio-mobile", Resolution = "audio", Codec = "none", AudioCodec = "aac", Container = "m4a", Bitrate = "96k", SizeGuidance = "Lower bitrate audiobook/music option" },
    ];

    [JsonPropertyName("scheduled_encodes_enabled")]
    public bool ScheduledEncodesEnabled { get; set; } = true;

    [JsonPropertyName("maintenance_window")]
    public string MaintenanceWindow { get; set; } = "01:00-05:00";

    [JsonPropertyName("variant_cache_path")]
    public string VariantCachePath { get; set; } = ".data/variants";

    [JsonPropertyName("variant_retention_days")]
    public int VariantRetentionDays { get; set; } = 30;

    [JsonPropertyName("cleanup_lru_enabled")]
    public bool CleanupLruEnabled { get; set; } = true;

    [JsonPropertyName("default_mobile_profile")]
    public string DefaultMobileProfile { get; set; } = "mobile-small";
}

/// <summary>A named transcoding quality profile.</summary>
public sealed class TranscodingQualityProfile
{
    [JsonPropertyName("name")]
    public string Name       { get; set; } = string.Empty;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string Codec      { get; set; } = "h264";

    [JsonPropertyName("audio_codec")]
    public string AudioCodec { get; set; } = "aac";

    [JsonPropertyName("container")]
    public string Container { get; set; } = "mp4";

    [JsonPropertyName("bitrate")]
    public string Bitrate    { get; set; } = string.Empty;

    [JsonPropertyName("size_guidance")]
    public string SizeGuidance { get; set; } = string.Empty;
}
