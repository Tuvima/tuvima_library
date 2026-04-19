using System.Diagnostics;
using System.Text.Json;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Processors;

/// <summary>
/// Locates FFmpeg binaries and exposes probing and command execution.
///
/// <para>
/// <b>Auto-detection order (first found wins):</b>
/// <list type="number">
///   <item><c>tools/ffmpeg/</c> relative to the app base directory — self-contained deployment.</item>
///   <item>System PATH — user-managed system-wide installation.</item>
///   <item>Explicit paths from <c>config/transcoding.json</c> — user override.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FFmpegService : IFFmpegService
{
    private readonly ILogger<FFmpegService> _logger;

    public string? FfmpegPath  { get; }
    public string? FfprobePath { get; }
    public bool    IsAvailable => FfmpegPath is not null && FfprobePath is not null;

    public HardwareCapabilities HardwareCapabilities { get; }

    public FFmpegService(IConfigurationLoader config, ILogger<FFmpegService> logger)
    {
        _logger = logger;

        var settings = config.LoadTranscoding();

        // ── Binary resolution ────────────────────────────────────────────────
        FfmpegPath  = ResolveBinary("ffmpeg",  settings.FfmpegBinaryPath);
        FfprobePath = ResolveBinary("ffprobe", settings.FfprobeBinaryPath);

        if (IsAvailable)
        {
            _logger.LogInformation("FFmpeg located: {Path}", FfmpegPath);
            HardwareCapabilities = DetectHardware(settings.HardwareAcceleration);
            _logger.LogInformation(
                "Hardware acceleration: {Accel} (preferred encoder: {Enc})",
                HardwareCapabilities.DetectedAccelerator ?? "none (software)",
                HardwareCapabilities.PreferredEncoder);
        }
        else
        {
            _logger.LogWarning(
                "FFmpeg binaries not found. Place ffmpeg.exe and ffprobe.exe in tools/ffmpeg/ " +
                "or install FFmpeg system-wide. Transcoding and video metadata extraction are disabled.");
            HardwareCapabilities = new HardwareCapabilities();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken ct = default)
    {
        if (!IsAvailable || !File.Exists(filePath)) return null;

        // ffprobe -v quiet -print_format json -show_format -show_streams -show_chapters
        var args = $"-v quiet -print_format json -show_format -show_streams -show_chapters \"{filePath}\"";
        var (exit, stdout, _) = await RunProcessAsync(FfprobePath!, args, ct).ConfigureAwait(false);
        if (exit != 0 || string.IsNullOrWhiteSpace(stdout)) return null;

        return ParseProbeJson(stdout, filePath);
    }

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string arguments, CancellationToken ct = default)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("FFmpeg is not available. Check tools/ffmpeg/ or system PATH.");

        return await RunProcessAsync(FfmpegPath!, arguments, ct).ConfigureAwait(false);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? ResolveBinary(string name, string? configuredPath)
    {
        // 1. Explicit config path
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
            return configuredPath;

        // 2. tools/ffmpeg/ relative to app base directory
        var appBase = AppContext.BaseDirectory;
        // Walk up from bin/Debug/net10.0/ to repo root looking for tools/ffmpeg/
        var dir = new DirectoryInfo(appBase);
        for (int depth = 0; depth < 6; depth++)
        {
            if (dir is null) break;
            var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg",
                OperatingSystem.IsWindows() ? $"{name}.exe" : name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        // 3. System PATH
        var onPath = FindOnPath(name);
        return onPath;
    }

    private static string? FindOnPath(string name)
    {
        var exe = OperatingSystem.IsWindows() ? $"{name}.exe" : name;
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return paths
            .Select(p => Path.Combine(p, exe))
            .FirstOrDefault(File.Exists);
    }

    private HardwareCapabilities DetectHardware(string mode)
    {
        if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
            return new HardwareCapabilities();

        try
        {
            // Query available encoders synchronously at startup.
            var psi = new ProcessStartInfo(FfmpegPath!, "-encoders -v quiet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return new HardwareCapabilities();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(10_000);

            bool hasNvenc     = output.Contains("h264_nvenc",     StringComparison.OrdinalIgnoreCase);
            bool hasQsv       = output.Contains("h264_qsv",       StringComparison.OrdinalIgnoreCase);
            bool hasVaapi     = output.Contains("h264_vaapi",     StringComparison.OrdinalIgnoreCase);

            // If mode is "auto", pick best; if mode is explicit, honour it.
            bool useNvenc  = hasNvenc  && (mode is "auto" or "nvenc");
            bool useQsv    = hasQsv    && (mode is "auto" or "quicksync") && !useNvenc;
            bool useVaapi  = hasVaapi  && (mode is "auto" or "vaapi")     && !useNvenc && !useQsv;

            string encoder    = useNvenc ? "h264_nvenc" : useQsv ? "h264_qsv" : useVaapi ? "h264_vaapi" : "libx264";
            string? accelName = useNvenc ? "NVIDIA NVENC" : useQsv ? "Intel Quick Sync" : useVaapi ? "VAAPI" : null;

            return new HardwareCapabilities
            {
                HasNvenc            = hasNvenc,
                HasQuickSync        = hasQsv,
                HasVaapi            = hasVaapi,
                PreferredEncoder    = encoder,
                DetectedAccelerator = accelName,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardware capability detection failed — using software encoding");
            return new HardwareCapabilities();
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string binary, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(binary, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdout = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        var stderr = await process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return (process.ExitCode, stdout, stderr);
    }

    private static MediaProbeResult? ParseProbeJson(string json, string filePath)
    {
        try
        {
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;

            // ── Format (container-level metadata) ────────────────────────────
            var format     = root.TryGetProperty("format", out var f) ? f : (JsonElement?)null;
            var tags       = format?.TryGetProperty("tags", out var t) == true ? t : (JsonElement?)null;

            double durationSec = 0;
            if (format?.TryGetProperty("duration", out var durEl) == true &&
                double.TryParse(durEl.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                durationSec = d;

            long fileSize = 0;
            if (format?.TryGetProperty("size", out var sizeEl) == true &&
                long.TryParse(sizeEl.GetString(), out var s))
                fileSize = s;

            string? Tag(string key)
            {
                if (tags is null) return null;
                foreach (var kv in new[] { key, key.ToUpperInvariant(), key.ToLowerInvariant() })
                    if (tags.Value.TryGetProperty(kv, out var v)) return v.GetString();
                return null;
            }

            // ── Streams ───────────────────────────────────────────────────────
            string? audioCodec = null;
            string? audioLanguage = null;
            int?    bitrate    = null;
            int?    sampleRate = null;
            int?    channels   = null;
            string? videoCodec = null;
            int?    width      = null;
            int?    height     = null;
            double? frameRate  = null;
            bool    hasCover   = false;
            var subtitleLanguages = new List<string>();

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var codecType = stream.TryGetProperty("codec_type", out var ct2) ? ct2.GetString() : null;
                    var codecName = stream.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;

                    if (codecType == "audio" && audioCodec is null)
                    {
                        audioCodec = codecName;
                        if (stream.TryGetProperty("tags", out var audioTags))
                        {
                            audioLanguage = TryReadLanguage(audioTags);
                        }
                        if (stream.TryGetProperty("bit_rate", out var br) &&
                            int.TryParse(br.GetString(), out var brv))
                            bitrate = brv / 1000;
                        if (stream.TryGetProperty("sample_rate", out var sr) &&
                            int.TryParse(sr.GetString(), out var srv))
                            sampleRate = srv;
                        if (stream.TryGetProperty("channels", out var ch))
                            channels = ch.GetInt32();
                    }
                    else if (codecType == "video")
                    {
                        // Check if this is an attached picture (cover art)
                        var disp = stream.TryGetProperty("disposition", out var dispEl) ? dispEl : (JsonElement?)null;
                        bool isAttached = disp?.TryGetProperty("attached_pic", out var ap) == true && ap.GetInt32() == 1;

                        if (isAttached)
                        {
                            hasCover = true;
                        }
                        else if (videoCodec is null)
                        {
                            videoCodec = codecName;
                            if (stream.TryGetProperty("width", out var w))  width  = w.GetInt32();
                            if (stream.TryGetProperty("height", out var h)) height = h.GetInt32();
                            if (stream.TryGetProperty("r_frame_rate", out var fr))
                            {
                                var frs = fr.GetString() ?? "0/1";
                                var parts = frs.Split('/');
                                if (parts.Length == 2 &&
                                    double.TryParse(parts[0], out var num) &&
                                    double.TryParse(parts[1], out var den) && den != 0)
                                    frameRate = Math.Round(num / den, 3);
                            }
                        }
                    }
                    else if (codecType == "subtitle")
                    {
                        string? subtitleLanguage = null;
                        if (stream.TryGetProperty("tags", out var subtitleTags))
                        {
                            subtitleLanguage = TryReadLanguage(subtitleTags);
                        }

                        subtitleLanguage ??= codecName;
                        if (!string.IsNullOrWhiteSpace(subtitleLanguage))
                        {
                            subtitleLanguages.Add(subtitleLanguage);
                        }
                    }
                }
            }

            // ── Chapters ──────────────────────────────────────────────────────
            int chapterCount = 0;
            if (root.TryGetProperty("chapters", out var chapters))
                chapterCount = chapters.GetArrayLength();

            return new MediaProbeResult
            {
                Duration       = TimeSpan.FromSeconds(durationSec),
                FileSizeBytes  = fileSize > 0 ? fileSize : new FileInfo(filePath).Length,
                Title          = Tag("title"),
                Artist         = Tag("artist"),
                Album          = Tag("album"),
                AlbumArtist    = Tag("album_artist"),
                Genre          = Tag("genre"),
                Date           = Tag("date"),
                Comment        = Tag("comment"),
                Narrator       = Tag("narrator") ?? Tag("composer"),
                Publisher      = Tag("publisher"),
                Description    = Tag("description") ?? Tag("comment"),
                AudioLanguage  = audioLanguage,
                AudioCodec     = audioCodec,
                AudioBitrate   = bitrate,
                SampleRate     = sampleRate,
                Channels       = channels,
                VideoCodec     = videoCodec,
                Width          = width,
                Height         = height,
                FrameRate      = frameRate,
                HasEmbeddedCover = hasCover,
                ChapterCount   = chapterCount,
                SubtitleLanguages = subtitleLanguages
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadLanguage(JsonElement tags)
    {
        foreach (var key in new[] { "language", "LANGUAGE", "Language" })
        {
            if (tags.TryGetProperty(key, out var value))
            {
                var raw = value.GetString();
                if (!string.IsNullOrWhiteSpace(raw))
                    return raw;
            }
        }

        return null;
    }
}
