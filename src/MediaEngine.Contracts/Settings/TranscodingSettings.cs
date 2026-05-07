using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Settings;

public sealed class TranscodingSettings
{
    [JsonPropertyName("ffmpeg_binary_path")]
    public string FfmpegBinaryPath { get; set; } = string.Empty;

    [JsonPropertyName("ffprobe_binary_path")]
    public string FfprobeBinaryPath { get; set; } = string.Empty;

    [JsonPropertyName("hardware_acceleration")]
    public string HardwareAcceleration { get; set; } = "auto";

    [JsonPropertyName("max_concurrent_transcodes")]
    public int MaxConcurrentTranscodes { get; set; } = 1;

    [JsonPropertyName("shadow_storage_limit_gb")]
    public int ShadowStorageLimitGb { get; set; } = 500;

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

public sealed class TranscodingQualityProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("resolution")]
    public string Resolution { get; set; } = string.Empty;

    [JsonPropertyName("codec")]
    public string Codec { get; set; } = "h264";

    [JsonPropertyName("audio_codec")]
    public string AudioCodec { get; set; } = "aac";

    [JsonPropertyName("container")]
    public string Container { get; set; } = "mp4";

    [JsonPropertyName("bitrate")]
    public string Bitrate { get; set; } = string.Empty;

    [JsonPropertyName("size_guidance")]
    public string SizeGuidance { get; set; } = string.Empty;
}
