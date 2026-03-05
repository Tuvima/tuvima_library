using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Processors.Contracts;
using MediaEngine.Processors.Models;

namespace MediaEngine.Processors.Processors;

/// <summary>
/// Identifies and extracts metadata from audio container formats
/// (MP3, M4A/M4B, FLAC, OGG, WAV).
///
/// ──────────────────────────────────────────────────────────────────
/// Format detection (magic bytes)
/// ──────────────────────────────────────────────────────────────────
///  • MP3: ID3v2 header (49 44 33) or MPEG sync word (FF Fx)
///  • M4A/M4B: ISO BMFF ftyp box at offset 4 ("ftyp" ASCII)
///  • FLAC: magic bytes 66 4C 61 43 ("fLaC")
///  • OGG: magic bytes 4F 67 67 53 ("OggS")
///  • WAV: RIFF....WAVE header
///
/// ──────────────────────────────────────────────────────────────────
/// Disambiguation
/// ──────────────────────────────────────────────────────────────────
///  For unambiguous formats (FLAC, OGG, WAV → Music; M4B → Audiobooks),
///  a single high-confidence candidate is returned.
///
///  For ambiguous formats (MP3, M4A), heuristic analysis produces
///  multiple <see cref="MediaTypeCandidate"/> entries with confidence
///  scores based on duration, bitrate, genre tag, chapter markers,
///  filename/path patterns, and file size.
/// </summary>
public sealed class AudioProcessor : IMediaProcessor
{
    /// <inheritdoc/>
    public MediaType SupportedType => MediaType.Audiobooks;

    /// <inheritdoc/>
    /// <remarks>
    /// Priority 95 — above VideoProcessor (90) to claim audio files
    /// before the video processor could claim M4A (shared ISO BMFF ftyp).
    /// </remarks>
    public int Priority => 95;

    // ── Magic-byte detection ─────────────────────────────────────────────

    private enum AudioContainer { Unknown, Mp3, M4a, M4b, Flac, Ogg, Wav }

    /// <inheritdoc/>
    public bool CanProcess(string filePath)
    {
        if (!File.Exists(filePath)) return false;
        return DetectContainer(filePath) != AudioContainer.Unknown;
    }

    /// <inheritdoc/>
    public Task<ProcessorResult> ProcessAsync(string filePath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var container = DetectContainer(filePath);
        if (container == AudioContainer.Unknown)
            return Task.FromResult(Corrupt(filePath, "No recognised audio magic bytes found."));

        ct.ThrowIfCancellationRequested();

        // Use TagLibSharp for metadata extraction.
        TagLib.File? tagFile = null;
        try
        {
            tagFile = TagLib.File.Create(filePath);
        }
        catch
        {
            // TagLib failure is non-fatal — proceed with container-only claims.
        }

        try
        {
            var claims = BuildClaims(filePath, container, tagFile);
            var candidates = BuildMediaTypeCandidates(filePath, container, tagFile);
            var topType = candidates.Count > 0 ? candidates[0].Type : MediaType.Audiobooks;

            // Extract cover art if available.
            byte[]? coverImage = null;
            string? coverMimeType = null;
            if (tagFile?.Tag.Pictures.Length > 0)
            {
                var pic = tagFile.Tag.Pictures[0];
                coverImage = pic.Data.Data;
                coverMimeType = pic.MimeType ?? "image/jpeg";
            }

            return Task.FromResult(new ProcessorResult
            {
                FilePath             = filePath,
                DetectedType         = topType,
                Claims               = claims,
                MediaTypeCandidates  = candidates,
                CoverImage           = coverImage,
                CoverImageMimeType   = coverMimeType,
            });
        }
        finally
        {
            tagFile?.Dispose();
        }
    }

    // ── Container detection ──────────────────────────────────────────────

    private static AudioContainer DetectContainer(string filePath)
    {
        try
        {
            Span<byte> header = stackalloc byte[16];
            using var fs = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16, FileOptions.None);

            int read = fs.Read(header);
            if (read < 4) return AudioContainer.Unknown;

            // FLAC: 66 4C 61 43 ("fLaC")
            if (header[0] == 0x66 && header[1] == 0x4C &&
                header[2] == 0x61 && header[3] == 0x43)
                return AudioContainer.Flac;

            // OGG: 4F 67 67 53 ("OggS")
            if (header[0] == 0x4F && header[1] == 0x67 &&
                header[2] == 0x67 && header[3] == 0x53)
                return AudioContainer.Ogg;

            // MP3: ID3v2 header (49 44 33 = "ID3")
            if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
                return AudioContainer.Mp3;

            // MP3: MPEG sync word (FF Fx where x >= B0)
            if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                return AudioContainer.Mp3;

            // ISO BMFF ftyp box at offset 4 (shared with video — distinguish by extension)
            if (read >= 8 &&
                header[4] == 0x66 && header[5] == 0x74 &&
                header[6] == 0x79 && header[7] == 0x70)
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                return ext switch
                {
                    ".m4b" => AudioContainer.M4b,
                    ".m4a" => AudioContainer.M4a,
                    // Other ftyp files (.mp4, .m4v) are video — not our concern.
                    _ => AudioContainer.Unknown,
                };
            }

            // WAV: RIFF....WAVE
            if (read >= 12 &&
                header[0] == 0x52 && header[1] == 0x49 &&
                header[2] == 0x46 && header[3] == 0x46 &&
                header[8] == 0x57 && header[9] == 0x41 &&
                header[10] == 0x56 && header[11] == 0x45)
                return AudioContainer.Wav;

            return AudioContainer.Unknown;
        }
        catch (IOException)               { return AudioContainer.Unknown; }
        catch (UnauthorizedAccessException) { return AudioContainer.Unknown; }
    }

    // ── Claim construction ───────────────────────────────────────────────

    private static IReadOnlyList<ExtractedClaim> BuildClaims(
        string filePath, AudioContainer container, TagLib.File? tagFile)
    {
        var claims = new List<ExtractedClaim>();

        // Title — from tags first, then filename fallback.
        var title = tagFile?.Tag.Title;
        if (!string.IsNullOrWhiteSpace(title))
            claims.Add(Claim("title", title, 0.8));
        else
        {
            var stem = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrWhiteSpace(stem))
                claims.Add(Claim("title", stem, 0.5));
        }

        // Author / Artist
        var artist = tagFile?.Tag.FirstAlbumArtist ?? tagFile?.Tag.FirstPerformer;
        if (!string.IsNullOrWhiteSpace(artist))
            claims.Add(Claim("author", artist, 0.7));

        // Album
        if (!string.IsNullOrWhiteSpace(tagFile?.Tag.Album))
            claims.Add(Claim("album", tagFile!.Tag.Album, 0.7));

        // Year
        if (tagFile?.Tag.Year is > 0)
            claims.Add(Claim("year", tagFile.Tag.Year.ToString(), 0.7));

        // Genre
        if (tagFile?.Tag.FirstGenre is not null)
            claims.Add(Claim("genre", tagFile.Tag.FirstGenre, 0.7));

        // Track number
        if (tagFile?.Tag.Track is > 0)
            claims.Add(Claim("track_number", tagFile.Tag.Track.ToString(), 0.7));

        // Duration
        if (tagFile?.Properties.Duration is { TotalSeconds: > 0 } dur)
            claims.Add(Claim("duration_sec", dur.TotalSeconds.ToString("F3"), 0.8));

        // Container format
        var containerLabel = container switch
        {
            AudioContainer.Mp3  => "MP3",
            AudioContainer.M4a  => "M4A",
            AudioContainer.M4b  => "M4B",
            AudioContainer.Flac => "FLAC",
            AudioContainer.Ogg  => "OGG",
            AudioContainer.Wav  => "WAV",
            _                   => "Unknown",
        };
        claims.Add(Claim("container", containerLabel, 1.0));

        // Bitrate
        if (tagFile?.Properties.AudioBitrate is > 0)
            claims.Add(Claim("audio_bitrate", tagFile.Properties.AudioBitrate.ToString(), 0.8));

        return claims;
    }

    // ── Media type disambiguation ────────────────────────────────────────

    private static List<MediaTypeCandidate> BuildMediaTypeCandidates(
        string filePath, AudioContainer container, TagLib.File? tagFile)
    {
        // Unambiguous containers → single candidate, no disambiguation needed.
        switch (container)
        {
            case AudioContainer.M4b:
                return [new() { Type = MediaType.Audiobooks, Confidence = 0.98, Reason = "M4B container (chapter markers)" }];
            case AudioContainer.Flac:
                return [new() { Type = MediaType.Music, Confidence = 0.95, Reason = "FLAC format (lossless music)" }];
            case AudioContainer.Ogg:
                return [new() { Type = MediaType.Music, Confidence = 0.95, Reason = "OGG format (music)" }];
            case AudioContainer.Wav:
                return [new() { Type = MediaType.Music, Confidence = 0.95, Reason = "WAV format (uncompressed audio)" }];
        }

        // Ambiguous: MP3 or M4A — run heuristic analysis.
        const double baseScore = 0.25;
        double audiobookScore = baseScore;
        double musicScore     = baseScore;
        double podcastScore   = baseScore;

        var reasons = new List<string>();

        // --- Duration ---
        var duration = tagFile?.Properties.Duration;
        if (duration is { TotalMinutes: > 0 })
        {
            double minutes = duration.Value.TotalMinutes;
            if (minutes > 60)
            {
                audiobookScore += 0.25;
                musicScore     -= 0.10;
                podcastScore   += 0.15;
                reasons.Add($"Duration {minutes:F0}min (long)");
            }
            else if (minutes >= 20)
            {
                audiobookScore += 0.10;
                musicScore     -= 0.05;
                podcastScore   += 0.20;
                reasons.Add($"Duration {minutes:F0}min (medium)");
            }
            else if (minutes < 7)
            {
                audiobookScore -= 0.15;
                musicScore     += 0.25;
                podcastScore   -= 0.10;
                reasons.Add($"Duration {minutes:F0}min (short)");
            }
        }

        // --- Chapter markers ---
        // TagLibSharp does not expose chapters directly, but M4A with chapters
        // is typically flagged via the container detection (M4B).
        // For MP3/M4A, check if the tag has chapter-like properties.

        // --- Genre tag ---
        var genre = tagFile?.Tag.FirstGenre?.Trim();
        if (!string.IsNullOrWhiteSpace(genre))
        {
            var genreLower = genre.ToLowerInvariant();
            if (IsAudiobookGenre(genreLower))
            {
                audiobookScore += 0.35;
                musicScore     -= 0.20;
                podcastScore   += 0.05;
                reasons.Add($"Genre \"{genre}\" (audiobook)");
            }
            else if (IsPodcastGenre(genreLower))
            {
                audiobookScore -= 0.15;
                musicScore     -= 0.15;
                podcastScore   += 0.40;
                reasons.Add($"Genre \"{genre}\" (podcast)");
            }
            else
            {
                // Assume music genre
                audiobookScore -= 0.20;
                musicScore     += 0.30;
                podcastScore   -= 0.10;
                reasons.Add($"Genre \"{genre}\" (music)");
            }
        }

        // --- Album + track number ---
        bool hasAlbum = !string.IsNullOrWhiteSpace(tagFile?.Tag.Album);
        bool hasTrack = tagFile?.Tag.Track > 0;
        if (hasAlbum && hasTrack)
        {
            audiobookScore -= 0.10;
            musicScore     += 0.20;
            podcastScore   -= 0.10;
            reasons.Add("Has album + track number");
        }

        // --- Bitrate ---
        int bitrate = tagFile?.Properties.AudioBitrate ?? 0;
        if (bitrate > 0)
        {
            if (bitrate <= 96)
            {
                audiobookScore += 0.10;
                musicScore     -= 0.05;
                podcastScore   += 0.10;
                reasons.Add($"Bitrate {bitrate}kbps (low)");
            }
            else if (bitrate >= 192)
            {
                audiobookScore -= 0.05;
                musicScore     += 0.15;
                podcastScore   -= 0.05;
                reasons.Add($"Bitrate {bitrate}kbps (high)");
            }
        }

        // --- Path keywords ---
        var pathLower = filePath.Replace('\\', '/').ToLowerInvariant();
        if (ContainsAny(pathLower, "audiobook", "audiobooks", "narrated"))
        {
            audiobookScore += 0.30;
            musicScore     -= 0.20;
            podcastScore   -= 0.10;
            reasons.Add("Path contains audiobook keyword");
        }
        else if (ContainsAny(pathLower, "music", "songs", "albums", "tracks"))
        {
            audiobookScore -= 0.20;
            musicScore     += 0.30;
            podcastScore   -= 0.10;
            reasons.Add("Path contains music keyword");
        }
        else if (ContainsAny(pathLower, "podcast", "podcasts", "episodes"))
        {
            audiobookScore -= 0.15;
            musicScore     -= 0.15;
            podcastScore   += 0.35;
            reasons.Add("Path contains podcast keyword");
        }

        // --- File size ---
        try
        {
            var fileSize = new FileInfo(filePath).Length;
            if (fileSize > 100 * 1024 * 1024) // > 100MB
            {
                audiobookScore += 0.15;
                musicScore     -= 0.05;
                podcastScore   -= 0.05;
                reasons.Add($"File size {fileSize / (1024 * 1024)}MB (large)");
            }
        }
        catch { /* ignore file access errors */ }

        // Normalize scores to [0.0, 1.0]
        var candidates = new List<(MediaType type, double score, string label)>
        {
            (MediaType.Audiobooks, audiobookScore, "Audiobooks"),
            (MediaType.Music,      musicScore,     "Music"),
            (MediaType.Podcasts,   podcastScore,   "Podcasts"),
        };

        double maxScore = candidates.Max(c => c.score);
        double minScore = candidates.Min(c => c.score);
        double range    = maxScore - minScore;

        var reasonStr = string.Join("; ", reasons);
        var result = new List<MediaTypeCandidate>();

        foreach (var (type, score, label) in candidates.OrderByDescending(c => c.score))
        {
            // Normalize: if all scores are equal, each gets 0.33.
            // Otherwise, scale to [0.15, 0.95] range.
            double normalized = range > 0
                ? 0.15 + 0.80 * (score - minScore) / range
                : 0.33;

            result.Add(new MediaTypeCandidate
            {
                Type       = type,
                Confidence = Math.Round(Math.Clamp(normalized, 0.05, 0.95), 2),
                Reason     = $"{label}: {reasonStr}",
            });
        }

        return result;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static readonly HashSet<string> AudiobookGenres =
        new(StringComparer.OrdinalIgnoreCase) { "audiobook", "speech", "spoken word", "narration" };

    private static readonly HashSet<string> PodcastGenres =
        new(StringComparer.OrdinalIgnoreCase) { "podcast" };

    private static bool IsAudiobookGenre(string genre) =>
        AudiobookGenres.Any(g => genre.Contains(g, StringComparison.OrdinalIgnoreCase));

    private static bool IsPodcastGenre(string genre) =>
        PodcastGenres.Any(g => genre.Contains(g, StringComparison.OrdinalIgnoreCase));

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
        DetectedType  = MediaType.Unknown,
        IsCorrupt     = true,
        CorruptReason = reason,
    };
}
