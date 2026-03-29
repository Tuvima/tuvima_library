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
///  All video containers are ambiguous between Movie and TV.
///  DetectedType is set to Unknown and MediaTypeCandidates is empty.
///  The IngestionEngine will call the AI MediaTypeAdvisor to classify
///  these files using filename, path, duration, and other signals.
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
    /// SupportedType is Movies for CanProcess routing purposes.
    /// ProcessAsync sets DetectedType to Unknown for all video files —
    /// the IngestionEngine will call the AI MediaTypeAdvisor to distinguish
    /// Movies from TV.
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

        // All video formats are ambiguous between Movie and TV.
        // Return empty candidates and Unknown type — IngestionEngine calls MediaTypeAdvisor.
        return new ProcessorResult
        {
            FilePath            = filePath,
            DetectedType        = MediaType.Unknown,
            Claims              = claims,
            MediaTypeCandidates = [],
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

    // ── Season/Episode regex patterns ──────────────────────────────────
    // Matched BEFORE title extraction so the series title (text before the
    // pattern) can be used as a cleaner title claim.

    /// <summary>S01E01, S01E01E02 (multi-episode), case-insensitive.</summary>
    private static readonly Regex SxxExxRegex = new(
        @"^(?<series>.+?)\s*[.\-_ ]*[Ss](?<season>\d{1,2})\s*[Ee](?<ep1>\d{1,4})(?:\s*[Ee](?<ep2>\d{1,4}))?",
        RegexOptions.Compiled);

    /// <summary>1x01 format.</summary>
    private static readonly Regex NxNNRegex = new(
        @"^(?<series>.+?)\s*[.\-_ ]+(?<season>\d{1,2})[Xx](?<ep1>\d{1,4})",
        RegexOptions.Compiled);

    /// <summary>Season 1 Episode 1 (verbose).</summary>
    private static readonly Regex VerboseRegex = new(
        @"^(?<series>.+?)\s*[.\-_ ]*Season\s*(?<season>\d{1,2})\s*Episode\s*(?<ep1>\d{1,4})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static IReadOnlyList<ExtractedClaim> BuildClaims(
        string filePath, VideoContainer container, VideoMetadata? meta)
    {
        var claims = new List<ExtractedClaim>();

        // Title — best-effort from filename stem.
        // SmartLabeler (Step 6b) handles intelligent parsing of year, season, episode, and quality tags.
        var stem = Path.GetFileNameWithoutExtension(filePath);
        if (!string.IsNullOrWhiteSpace(stem))
        {
            // Basic filename cleanup — SmartLabeler (Step 6b) handles intelligent parsing.
            var basicTitle = stem.Replace('.', ' ').Replace('_', ' ').Trim();

            // Attempt season/episode extraction BEFORE the title-only path.
            // When detected, the series title (text before the episode pattern) is
            // used as a cleaner title and season_number / episode_number are emitted.
            var (seriesTitle, seasonNum, episodeNum) = ExtractSeasonEpisode(basicTitle);

            if (seriesTitle is not null && seasonNum.HasValue)
            {
                claims.Add(Claim("title", seriesTitle, 0.55));
                claims.Add(Claim("season_number", seasonNum.Value.ToString(), 0.55));
                if (episodeNum.HasValue)
                    claims.Add(Claim("episode_number", episodeNum.Value.ToString(), 0.55));
            }
            else if (!string.IsNullOrWhiteSpace(basicTitle))
            {
                claims.Add(Claim("title", basicTitle, 0.50));
            }
        }

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
    // Season/Episode extraction
    // -------------------------------------------------------------------------

    /// <summary>
    /// Attempts to extract series title, season number, and episode number from
    /// a cleaned filename stem using common TV naming patterns:
    ///   S01E01, S01E01E02, 1x01, Season 1 Episode 1.
    /// Returns (null, null, null) when no pattern matches.
    /// </summary>
    private static (string? SeriesTitle, int? Season, int? Episode) ExtractSeasonEpisode(string text)
    {
        // Try S01E01 / S01E01E02 first (most common).
        var m = SxxExxRegex.Match(text);
        if (m.Success)
        {
            var series = CleanSeriesTitle(m.Groups["series"].Value);
            var season = int.Parse(m.Groups["season"].Value);
            var episode = int.Parse(m.Groups["ep1"].Value);
            return (series, season, episode);
        }

        // Try 1x01 format.
        m = NxNNRegex.Match(text);
        if (m.Success)
        {
            var series = CleanSeriesTitle(m.Groups["series"].Value);
            var season = int.Parse(m.Groups["season"].Value);
            var episode = int.Parse(m.Groups["ep1"].Value);
            return (series, season, episode);
        }

        // Try verbose "Season 1 Episode 1".
        m = VerboseRegex.Match(text);
        if (m.Success)
        {
            var series = CleanSeriesTitle(m.Groups["series"].Value);
            var season = int.Parse(m.Groups["season"].Value);
            var episode = int.Parse(m.Groups["ep1"].Value);
            return (series, season, episode);
        }

        return (null, null, null);
    }

    /// <summary>Trims trailing separators and whitespace from extracted series title.</summary>
    private static string CleanSeriesTitle(string raw)
    {
        return raw.TrimEnd('.', '-', '_', ' ');
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
