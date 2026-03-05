using System.Text.RegularExpressions;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Processors;

/// <summary>
/// Identifies and extracts metadata from video container formats
/// (MP4/M4V, MKV/WebM, AVI).
///
/// ──────────────────────────────────────────────────────────────────
/// Format detection (spec: Phase 5 – Magic-Byte Detection)
/// ──────────────────────────────────────────────────────────────────
///  • MP4 / M4V / QuickTime: bytes 4-7 = "ftyp" (ASCII)
///    All modern MP4 files begin with an <c>ftyp</c> ISO Base Media box.
///  • MKV / WebM: bytes 0-3 = 0x1A 0x45 0xDF 0xA3 (EBML header)
///  • AVI: bytes 0-3 = "RIFF" + bytes 8-11 = "AVI " (RIFF/AVI)
///
///  At least 12 bytes are read for detection; no more than 16 are needed.
///
/// ──────────────────────────────────────────────────────────────────
/// Disambiguation (Movie vs TV)
/// ──────────────────────────────────────────────────────────────────
///  All video containers are ambiguous between Movie and TV. Heuristic
///  signals (filename patterns, duration, path keywords, file size)
///  produce <see cref="MediaTypeCandidate"/> entries with confidence
///  scores. The top candidate becomes <see cref="ProcessorResult.DetectedType"/>.
///
/// ──────────────────────────────────────────────────────────────────
/// Metadata extraction (spec: Phase 5 – Content Extraction; FFmpeg stub)
/// ──────────────────────────────────────────────────────────────────
///  Extraction is delegated to <see cref="IVideoMetadataExtractor"/>.
///  The current default is <see cref="StubVideoMetadataExtractor"/>
///  which returns an empty <see cref="VideoMetadata"/>; replace it with
///  <c>FFmpegVideoMetadataExtractor</c> once FFmpeg is integrated.
///
///  Extracted claims:
///   • title         (confidence 0.5 — filename stem fallback)
///   • container     (confidence 1.0 — from magic bytes: "MP4", "MKV", "AVI")
///   • video_width   (confidence 0.8 — from extractor, omitted when null)
///   • video_height  (confidence 0.8 — from extractor, omitted when null)
///   • duration_sec  (confidence 0.8 — total seconds as decimal string)
///   • video_codec   (confidence 0.8 — short codec name, e.g. "h264")
///   • frame_rate    (confidence 0.8 — fps as decimal string)
///
/// Spec: Phase 5 – Media Processor Architecture § VideoProcessor (stub).
/// </summary>
public sealed class VideoProcessor : IMediaProcessor
{
    private readonly IVideoMetadataExtractor _extractor;

    public VideoProcessor(IVideoMetadataExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        _extractor = extractor;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Defaults to <see cref="MediaType.Movies"/>; when disambiguation
    /// produces a top candidate of <see cref="MediaType.TV"/>, that type
    /// is used instead.
    /// </remarks>
    public MediaType SupportedType => MediaType.Movies;

    /// <inheritdoc/>
    /// <remarks>
    /// Priority 90 — below EpubProcessor (100) and AudioProcessor (95),
    /// above ComicProcessor (85).
    /// </remarks>
    public int Priority => 90;

    // -------------------------------------------------------------------------
    // IMediaProcessor
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public bool CanProcess(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        return DetectContainer(filePath) != VideoContainer.Unknown;
    }

    /// <inheritdoc/>
    public async Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var container = DetectContainer(filePath);
        if (container == VideoContainer.Unknown)
            return Corrupt(filePath, "No recognised video magic bytes found.");

        VideoMetadata? meta = null;
        try
        {
            meta = await _extractor.ExtractAsync(filePath, ct).ConfigureAwait(false);
        }
        catch
        {
            // Extraction failure is non-fatal; we still have format + filename claims.
        }

        ct.ThrowIfCancellationRequested();

        var claims = BuildClaims(filePath, container, meta);
        var candidates = BuildMediaTypeCandidates(filePath, meta);
        var topType = candidates.Count > 0 ? candidates[0].Type : MediaType.Movies;

        return new ProcessorResult
        {
            FilePath            = filePath,
            DetectedType        = topType,
            Claims              = claims,
            MediaTypeCandidates = candidates,
        };
    }

    // -------------------------------------------------------------------------
    // Magic-byte detection
    // -------------------------------------------------------------------------

    private enum VideoContainer { Unknown, Mp4, Mkv, Avi }

    private static VideoContainer DetectContainer(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[16];
            using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16, FileOptions.None);

            int read = fs.Read(header);
            if (read < 4) return VideoContainer.Unknown;

            // MKV / WebM: EBML header  1A 45 DF A3
            if (header[0] == 0x1A && header[1] == 0x45 &&
                header[2] == 0xDF && header[3] == 0xA3)
                return VideoContainer.Mkv;

            // MP4 / M4V / QuickTime: ISO BMFF ftyp box at offset 4
            if (read >= 8 &&
                header[4] == 0x66 && header[5] == 0x74 &&   // 'f' 't'
                header[6] == 0x79 && header[7] == 0x70)      // 'y' 'p'
                return VideoContainer.Mp4;

            // AVI: RIFF....AVI
            if (read >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 &&   // 'R' 'I'
                header[2] == 0x46 && header[3] == 0x46 &&   // 'F' 'F'
                header[8] == 0x41 && header[9] == 0x56 &&   // 'A' 'V'
                header[10] == 0x49 && header[11] == 0x20)    // 'I' ' '
                return VideoContainer.Avi;

            return VideoContainer.Unknown;
        }
        catch (IOException)               { return VideoContainer.Unknown; }
        catch (UnauthorizedAccessException) { return VideoContainer.Unknown; }
    }

    // -------------------------------------------------------------------------
    // Claim construction
    // -------------------------------------------------------------------------

    private static IReadOnlyList<ExtractedClaim> BuildClaims(
        string filePath, VideoContainer container, VideoMetadata? meta)
    {
        var claims = new List<ExtractedClaim>();

        // Title — best-effort from filename stem.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(stem))
            claims.Add(Claim("title", stem, 0.5));

        // Container format — authoritative from magic bytes.
        var containerLabel = container switch
        {
            VideoContainer.Mp4 => "MP4",
            VideoContainer.Mkv => "MKV",
            VideoContainer.Avi => "AVI",
            _                  => "Unknown",
        };
        claims.Add(Claim("container", containerLabel, 1.0));

        // Technical claims from extractor (emitted only when not null).
        if (meta is not null)
        {
            if (meta.WidthPx.HasValue)
                claims.Add(Claim("video_width", meta.WidthPx.Value.ToString(), 0.8));

            if (meta.HeightPx.HasValue)
                claims.Add(Claim("video_height", meta.HeightPx.Value.ToString(), 0.8));

            if (meta.Duration.HasValue)
                claims.Add(Claim("duration_sec",
                    meta.Duration.Value.TotalSeconds.ToString("F3"), 0.8));

            if (!string.IsNullOrWhiteSpace(meta.VideoCodec))
                claims.Add(Claim("video_codec", meta.VideoCodec, 0.8));

            if (meta.FrameRate.HasValue)
                claims.Add(Claim("frame_rate",
                    meta.FrameRate.Value.ToString("F3"), 0.8));
        }

        return claims;
    }

    // -------------------------------------------------------------------------
    // Media type disambiguation (Movie vs TV)
    // -------------------------------------------------------------------------

    /// <summary>Default TV filename patterns (SxxExx, 1x01, etc.).</summary>
    private static readonly Regex[] TvFilenamePatterns =
    [
        new(@"S\d{2}E\d{2}", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\d{1,2}x\d{2}", RegexOptions.Compiled),
    ];

    private static List<MediaTypeCandidate> BuildMediaTypeCandidates(
        string filePath, VideoMetadata? meta)
    {
        const double baseScore = 0.35;
        double movieScore = baseScore;
        double tvScore    = baseScore;

        var reasons = new List<string>();

        var filename = Path.GetFileNameWithoutExtension(filePath) ?? "";

        // --- Filename pattern: SxxExx or NxNN ---
        bool hasTvPattern = false;
        foreach (var pattern in TvFilenamePatterns)
        {
            if (pattern.IsMatch(filename))
            {
                hasTvPattern = true;
                break;
            }
        }

        if (hasTvPattern)
        {
            movieScore -= 0.30;
            tvScore    += 0.35;
            reasons.Add("Filename matches TV pattern (SxxExx)");
        }

        // --- Duration ---
        if (meta?.Duration is { TotalMinutes: > 0 } dur)
        {
            double minutes = dur.TotalMinutes;
            if (minutes > 60)
            {
                movieScore += 0.20;
                tvScore    -= 0.05;
                reasons.Add($"Duration {minutes:F0}min (long, typical movie)");
            }
            else if (minutes is >= 15 and <= 45)
            {
                movieScore -= 0.10;
                tvScore    += 0.20;
                reasons.Add($"Duration {minutes:F0}min (typical TV episode)");
            }
        }

        // --- Path keywords ---
        var pathLower = filePath.Replace('\\', '/').ToLowerInvariant();
        if (ContainsAny(pathLower, "season", "series", "tv", "show", "shows"))
        {
            movieScore -= 0.25;
            tvScore    += 0.30;
            reasons.Add("Path contains TV keyword");
        }
        else if (ContainsAny(pathLower, "movie", "movies", "film", "films"))
        {
            movieScore += 0.25;
            tvScore    -= 0.20;
            reasons.Add("Path contains movie keyword");
        }

        // --- File size ---
        try
        {
            var fileSizeBytes = new FileInfo(filePath).Length;
            double fileSizeGb = fileSizeBytes / (1024.0 * 1024 * 1024);
            if (fileSizeGb > 4)
            {
                movieScore += 0.15;
                tvScore    -= 0.05;
                reasons.Add($"File size {fileSizeGb:F1}GB (large, typical movie)");
            }
        }
        catch { /* ignore file access errors */ }

        // --- Multiple similarly-named files in folder (episode batch) ---
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir is not null)
            {
                var ext = Path.GetExtension(filePath);
                var siblings = Directory.GetFiles(dir, $"*{ext}")
                    .Where(f => !string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (siblings.Length >= 3)
                {
                    movieScore -= 0.10;
                    tvScore    += 0.20;
                    reasons.Add($"{siblings.Length + 1} video files in folder (episode batch)");
                }
            }
        }
        catch { /* ignore directory access errors */ }

        // Normalize scores to [0.0, 1.0]
        var candidates = new List<(MediaType type, double score, string label)>
        {
            (MediaType.Movies, movieScore, "Movie"),
            (MediaType.TV,     tvScore,    "TV"),
        };

        double maxScore = candidates.Max(c => c.score);
        double minScore = candidates.Min(c => c.score);
        double range    = maxScore - minScore;

        var reasonStr = reasons.Count > 0
            ? string.Join("; ", reasons)
            : "No strong signals";

        var result = new List<MediaTypeCandidate>();

        foreach (var (type, score, label) in candidates.OrderByDescending(c => c.score))
        {
            // Normalize: if scores are equal, each gets 0.50.
            // Otherwise, scale to [0.20, 0.90] range.
            double normalized = range > 0
                ? 0.20 + 0.70 * (score - minScore) / range
                : 0.50;

            result.Add(new MediaTypeCandidate
            {
                Type       = type,
                Confidence = Math.Round(Math.Clamp(normalized, 0.10, 0.95), 2),
                Reason     = $"{label}: {reasonStr}",
            });
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool ContainsAny(string text, params string[] keywords)
    {
        foreach (var kw in keywords)
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static ExtractedClaim Claim(string key, string value, double confidence) => new()
    {
        Key        = key,
        Value      = value,
        Confidence = confidence,
    };

    private static ProcessorResult Corrupt(string filePath, string reason) => new()
    {
        FilePath      = filePath,
        DetectedType  = MediaType.Movies,
        IsCorrupt     = true,
        CorruptReason = reason,
    };
}
