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
    public string FfmpegBinaryPath  { get; set; } = string.Empty;

    /// <summary>
    /// Explicit path to ffprobe.exe / ffprobe binary.
    /// Leave empty to enable auto-detection (tools/ffmpeg/ → PATH).
    /// </summary>
    public string FfprobeBinaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Hardware acceleration mode: "auto" | "nvenc" | "quicksync" | "vaapi" | "none".
    /// "auto" probes available encoders and selects the best option with software fallback.
    /// </summary>
    public string HardwareAcceleration { get; set; } = "auto";

    /// <summary>Maximum number of concurrent transcoding jobs.</summary>
    public int MaxConcurrentTranscodes { get; set; } = 1;

    /// <summary>Maximum disk space (GB) for shadow transcoded copies.</summary>
    public int ShadowStorageLimitGb { get; set; } = 500;

    /// <summary>Quality profiles available for transcoding.</summary>
    public List<TranscodingQualityProfile> QualityProfiles { get; set; } =
    [
        new() { Name = "mobile",  Resolution = "720p",  Codec = "h264", Bitrate = "2M" },
        new() { Name = "tablet",  Resolution = "1080p", Codec = "h264", Bitrate = "5M" },
    ];
}

/// <summary>A named transcoding quality profile.</summary>
public sealed class TranscodingQualityProfile
{
    public string Name       { get; set; } = string.Empty;
    public string Resolution { get; set; } = string.Empty;
    public string Codec      { get; set; } = "h264";
    public string Bitrate    { get; set; } = string.Empty;
}
