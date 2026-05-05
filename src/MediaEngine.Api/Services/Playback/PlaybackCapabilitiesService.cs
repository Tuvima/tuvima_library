using System.Globalization;
using System.Text.Json;
using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using MediaInfo;

namespace MediaEngine.Api.Services.Playback;

public sealed class PlaybackCapabilitiesService
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi",
    };

    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".m4a", ".m4b", ".aac", ".flac", ".ogg", ".wav",
    };

    private static readonly HashSet<string> BookExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".epub", ".pdf",
    };

    private static readonly HashSet<string> ComicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cbz", ".cbr",
    };

    private readonly IMediaAssetRepository _assets;
    private readonly IFFmpegService _ffmpeg;
    private readonly IDatabaseConnection _db;
    private readonly PlaybackStateRepository _playbackState;
    private readonly IPlaybackSegmentRepository _segments;
    private readonly ILogger<PlaybackCapabilitiesService> _logger;

    public PlaybackCapabilitiesService(
        IMediaAssetRepository assets,
        IFFmpegService ffmpeg,
        IDatabaseConnection db,
        PlaybackStateRepository playbackState,
        IPlaybackSegmentRepository segments,
        ILogger<PlaybackCapabilitiesService> logger)
    {
        _assets = assets;
        _ffmpeg = ffmpeg;
        _db = db;
        _playbackState = playbackState;
        _segments = segments;
        _logger = logger;
    }

    public async Task<PlaybackManifestDto?> BuildManifestAsync(Guid assetId, string? client, CancellationToken ct = default)
    {
        var asset = await _assets.FindByIdAsync(assetId, ct);
        if (asset is null)
        {
            return null;
        }

        var normalizedClient = NormalizeClient(client);
        var profile = ProfileFor(normalizedClient);
        var extension = Path.GetExtension(asset.FilePathRoot);
        var mediaType = await ResolveMediaTypeAsync(asset, extension, ct);
        var warnings = new List<string>();

        if (!File.Exists(asset.FilePathRoot))
        {
            warnings.Add("Source file is missing on disk.");
        }

        MediaProbeResult? probe = null;
        MediaInfoWrapper? mediaInfo = null;
        if (File.Exists(asset.FilePathRoot) && _ffmpeg.IsAvailable && (VideoExtensions.Contains(extension) || AudioExtensions.Contains(extension)))
        {
            try
            {
                probe = await _ffmpeg.ProbeAsync(asset.FilePathRoot, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FFprobe failed for playback manifest asset {AssetId}", assetId);
                warnings.Add("Media inspection failed; manifest decisions are based on file extension only.");
            }
        }
        else if (!_ffmpeg.IsAvailable && (VideoExtensions.Contains(extension) || AudioExtensions.Contains(extension)))
        {
            warnings.Add("FFmpeg/FFprobe is unavailable; codec-level direct-play decisions are limited.");
        }

        if (File.Exists(asset.FilePathRoot) && (VideoExtensions.Contains(extension) || AudioExtensions.Contains(extension)))
        {
            try
            {
                var inspected = new MediaInfoWrapper(asset.FilePathRoot, null);
                if (inspected.Success)
                {
                    mediaInfo = inspected;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "MediaInfo inspection failed for asset {AssetId}", assetId);
            }
        }

        var sourceHash = BuildSourceHash(asset);
        if (File.Exists(asset.FilePathRoot))
        {
            var info = new FileInfo(asset.FilePathRoot);
            await _playbackState.StoreInspectionAsync(
                assetId,
                sourceHash,
                info.Length,
                mediaInfo?.Duration > 0 ? mediaInfo.Duration : probe?.Duration.TotalSeconds,
                mediaInfo?.Format ?? extension.TrimStart('.').ToLowerInvariant(),
                mediaInfo is not null ? mediaInfo.Text : probe is null ? null : JsonSerializer.Serialize(probe),
                ct);
        }

        var directPlay = IsDirectPlaySupported(extension, profile, mediaInfo, probe);
        var recommendedDelivery = GetRecommendedDelivery(mediaType, normalizedClient, directPlay);
        var conversionReason = directPlay
            ? null
            : GetConversionReason(extension, profile, mediaInfo, probe);
        if (recommendedDelivery == PlaybackDeliveryModes.Hls)
        {
            warnings.Add("HLS generation is planned for this profile but no generated HLS variant is available yet.");
        }

        var variants = await _playbackState.ListOfflineVariantsAsync(assetId, sourceHash, ct);

        return new PlaybackManifestDto
        {
            AssetId = assetId,
            Client = normalizedClient,
            MediaType = mediaType,
            SourceExtension = extension,
            RecommendedDelivery = recommendedDelivery,
            DirectPlaySupported = directPlay,
            DirectStreamUrl = File.Exists(asset.FilePathRoot) ? $"/stream/{assetId}" : null,
            HlsUrl = null,
            Profile = profile,
            AudioTracks = BuildAudioTracks(mediaInfo, probe),
            SubtitleTracks = BuildSubtitleTracks(mediaInfo, probe),
            Chapters = BuildChapters(mediaInfo, probe),
            OfflineVariants = variants,
            Resume = await LoadResumeAsync(assetId, ct),
            Segments = (await _segments.ListByAssetAsync(assetId, ct)).Select(ToSegmentDto).ToList(),
            Warnings = warnings,
            ConversionReason = conversionReason,
        };
    }

    public async Task<EncodeJobDto?> QueueEncodeAsync(Guid assetId, QueueEncodeRequestDto request, CancellationToken ct = default)
    {
        var asset = await _assets.FindByIdAsync(assetId, ct);
        if (asset is null)
        {
            return null;
        }

        var profileKey = string.IsNullOrWhiteSpace(request.ProfileKey)
            ? "mobile-standard"
            : request.ProfileKey.Trim().ToLowerInvariant();

        return await _playbackState.QueueEncodeJobAsync(assetId, profileKey, BuildSourceHash(asset), request.ScheduledFor, ct);
    }

    public Task<IReadOnlyList<EncodeJobDto>> ListEncodeJobsAsync(CancellationToken ct = default) =>
        _playbackState.ListEncodeJobsAsync(ct);

    public Task CancelEncodeJobAsync(Guid jobId, CancellationToken ct = default) =>
        _playbackState.CancelEncodeJobAsync(jobId, ct);

    public async Task<PlaybackDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        var activeJobs = await _playbackState.ListActiveEncodeJobsAsync(ct);
        var warnings = new List<string>();
        if (!_ffmpeg.IsAvailable)
        {
            warnings.Add("FFmpeg is not available. Direct file streaming can work, but HLS, transcoding, thumbnails, and offline variants cannot be generated.");
        }

        return new PlaybackDiagnosticsDto
        {
            FFmpegAvailable = _ffmpeg.IsAvailable,
            FFmpegVersion = _ffmpeg.FfmpegPath,
            MediaInfoAvailable = TryGetMediaInfoVersion(out var mediaInfoVersion),
            MediaInfoVersion = mediaInfoVersion,
            ActiveJobs = activeJobs,
            Warnings = warnings,
        };
    }

    public Task<OfflineVariantFile?> GetOfflineVariantFileAsync(Guid assetId, Guid variantId, CancellationToken ct = default) =>
        _playbackState.GetOfflineVariantFileAsync(assetId, variantId, ct);

    private async Task<string> ResolveMediaTypeAsync(MediaAsset asset, string extension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var mediaType = conn.QueryFirstOrDefault<string>("""
            SELECT w.media_type
            FROM media_assets ma
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            WHERE ma.id = @assetId
            LIMIT 1;
            """,
            new { assetId = asset.Id.ToString() });

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            return mediaType;
        }

        if (VideoExtensions.Contains(extension)) return "Movies";
        if (AudioExtensions.Contains(extension)) return string.Equals(extension, ".m4b", StringComparison.OrdinalIgnoreCase) ? "Audiobooks" : "Music";
        if (BookExtensions.Contains(extension)) return "Books";
        if (ComicExtensions.Contains(extension)) return "Comics";
        return "Unknown";
    }

    private async Task<PlaybackResumeDto?> LoadResumeAsync(Guid assetId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ResumeRow>("""
            SELECT progress_pct AS ProgressPct,
                   last_accessed AS LastAccessed
            FROM user_states
            WHERE asset_id = @assetId
            ORDER BY last_accessed DESC
            LIMIT 1;
            """,
            new { assetId = assetId.ToString() });

        await Task.CompletedTask;
        if (row is null)
        {
            return null;
        }

        return new PlaybackResumeDto
        {
            ProgressPct = row.ProgressPct,
            LastAccessed = DateTimeOffset.TryParse(row.LastAccessed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null,
        };
    }

    private static string BuildSourceHash(MediaAsset asset)
    {
        if (!File.Exists(asset.FilePathRoot))
        {
            return asset.ContentHash;
        }

        var info = new FileInfo(asset.FilePathRoot);
        return $"{asset.ContentHash}:{info.Length}:{info.LastWriteTimeUtc.Ticks}";
    }

    private static bool IsDirectPlaySupported(string extension, PlaybackProfileDto profile, MediaInfoWrapper? mediaInfo, MediaProbeResult? probe)
    {
        var container = extension.TrimStart('.').ToLowerInvariant();
        if (!profile.SupportedContainers.Contains(container, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var videoCodec = NormalizeCodecName(mediaInfo?.VideoCodec ?? probe?.VideoCodec);
        var audioCodec = NormalizeCodecName(mediaInfo?.AudioCodec ?? probe?.AudioCodec);
        var height = mediaInfo?.Height > 0 ? mediaInfo.Height : probe?.Height;

        if (!string.IsNullOrWhiteSpace(videoCodec)
            && profile.SupportedVideoCodecs.Count > 0
            && !profile.SupportedVideoCodecs.Contains(videoCodec, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(audioCodec)
            && profile.SupportedAudioCodecs.Count > 0
            && !profile.SupportedAudioCodecs.Contains(audioCodec, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (profile.MaxHeight.HasValue && height > profile.MaxHeight.Value)
        {
            return false;
        }

        return true;
    }

    private static string GetRecommendedDelivery(string mediaType, string client, bool directPlay)
    {
        if (mediaType.Equals("Books", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("Comics", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackDeliveryModes.Reader;
        }

        if (client.Equals("mobile-download", StringComparison.OrdinalIgnoreCase))
        {
            return PlaybackDeliveryModes.OfflineVariant;
        }

        return directPlay ? PlaybackDeliveryModes.DirectStream : PlaybackDeliveryModes.Hls;
    }

    private static string GetConversionReason(string extension, PlaybackProfileDto profile, MediaInfoWrapper? mediaInfo, MediaProbeResult? probe)
    {
        var container = extension.TrimStart('.').ToLowerInvariant();
        var videoCodec = NormalizeCodecName(mediaInfo?.VideoCodec ?? probe?.VideoCodec);
        var audioCodec = NormalizeCodecName(mediaInfo?.AudioCodec ?? probe?.AudioCodec);
        var height = mediaInfo?.Height > 0 ? mediaInfo.Height : probe?.Height;

        if (!profile.SupportedContainers.Contains(container, StringComparer.OrdinalIgnoreCase))
        {
            return $"{profile.DisplayName} does not direct-play .{container} containers.";
        }

        if (!string.IsNullOrWhiteSpace(videoCodec)
            && !profile.SupportedVideoCodecs.Contains(videoCodec, StringComparer.OrdinalIgnoreCase))
        {
            return $"{profile.DisplayName} does not direct-play {videoCodec} video.";
        }

        if (!string.IsNullOrWhiteSpace(audioCodec)
            && !profile.SupportedAudioCodecs.Contains(audioCodec, StringComparer.OrdinalIgnoreCase))
        {
            return $"{profile.DisplayName} does not direct-play {audioCodec} audio.";
        }

        if (profile.MaxHeight.HasValue && height > profile.MaxHeight.Value)
        {
            return $"{profile.DisplayName} profile is capped at {profile.MaxHeight.Value}p.";
        }

        return "The client profile requires a compatible HLS or offline variant.";
    }

    private static IReadOnlyList<PlaybackTrackDto> BuildAudioTracks(MediaInfoWrapper? mediaInfo, MediaProbeResult? probe)
    {
        if (mediaInfo?.AudioStreams.Count > 0)
        {
            return mediaInfo.AudioStreams.Select((stream, index) => new PlaybackTrackDto
            {
                Index = index,
                Kind = "audio",
                Language = FirstNonBlank(stream.LanguageIetf, stream.Language),
                Codec = NormalizeCodecName(stream.Codec.ToString()),
                DisplayName = FirstNonBlank(stream.Name, stream.LanguageIetf, stream.Language, $"Audio {index + 1}"),
                IsDefault = stream.Default || index == 0,
                Channels = stream.Channel > 0 ? stream.Channel : null,
                BitrateKbps = stream.Bitrate > 0 ? (int)Math.Round(stream.Bitrate / 1000d) : null,
            }).ToList();
        }

        if (probe is null || string.IsNullOrWhiteSpace(probe.AudioCodec))
        {
            return [];
        }

        return
        [
            new PlaybackTrackDto
            {
                Index = 0,
                Kind = "audio",
                Language = probe.AudioLanguage,
                Codec = NormalizeCodecName(probe.AudioCodec),
                DisplayName = string.IsNullOrWhiteSpace(probe.AudioLanguage) ? "Default audio" : $"Audio ({probe.AudioLanguage})",
                IsDefault = true,
                Channels = probe.Channels,
                BitrateKbps = probe.AudioBitrate,
            }
        ];
    }

    private static IReadOnlyList<PlaybackSubtitleTrackDto> BuildSubtitleTracks(MediaInfoWrapper? mediaInfo, MediaProbeResult? probe)
    {
        if (mediaInfo?.Subtitles.Count > 0)
        {
            return mediaInfo.Subtitles.Select((stream, index) => new PlaybackSubtitleTrackDto
            {
                Index = index,
                Language = FirstNonBlank(stream.LanguageIetf, stream.Language),
                Codec = NormalizeCodecName(stream.Codec.ToString()),
                DisplayName = FirstNonBlank(stream.Name, stream.LanguageIetf, stream.Language, $"Subtitles {index + 1}"),
                IsDefault = stream.Default,
                IsForced = stream.Forced,
            }).ToList();
        }

        if (probe?.SubtitleLanguages.Count is not > 0)
        {
            return [];
        }

        return probe.SubtitleLanguages.Select((language, index) => new PlaybackSubtitleTrackDto
        {
            Index = index,
            Language = language,
            DisplayName = $"Subtitles ({language})",
            IsDefault = index == 0,
        }).ToList();
    }

    private static IReadOnlyList<PlaybackChapterDto> BuildChapters(MediaInfoWrapper? mediaInfo, MediaProbeResult? probe)
    {
        if (mediaInfo?.Chapters.Count > 0)
        {
            return mediaInfo.Chapters.Select((chapter, index) => new PlaybackChapterDto
            {
                Index = index,
                Title = FirstNonBlank(chapter.Name, $"Chapter {index + 1}"),
                StartSeconds = 0,
            }).ToList();
        }

        if (probe?.ChapterCount is not > 0)
        {
            return [];
        }

        return Enumerable.Range(0, probe.ChapterCount)
            .Select(index => new PlaybackChapterDto
            {
                Index = index,
                Title = $"Chapter {index + 1}",
                StartSeconds = 0,
            })
            .ToList();
    }

    private static string NormalizeCodecName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace("_", string.Empty).Replace("-", string.Empty);
        return normalized switch
        {
            "avc" or "mpeg4avc" or "h264" => "h264",
            "hevc" or "h265" => "hevc",
            "aaclc" or "aac" => "aac",
            "mpeg1audiolayer3" or "mp3" => "mp3",
            "pcm" or "pcms16le" => "pcm_s16le",
            "vorbis" => "vorbis",
            "opus" => "opus",
            "flac" => "flac",
            "vp8" => "vp8",
            "vp9" => "vp9",
            "av1" => "av1",
            _ => value.Trim().ToLowerInvariant(),
        };
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool TryGetMediaInfoVersion(out string? version)
    {
        try
        {
            version = typeof(MediaInfoWrapper).Assembly.GetName().Version?.ToString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            version = null;
            return false;
        }
    }

    private static string NormalizeClient(string? client) =>
        string.IsNullOrWhiteSpace(client) ? "web" : client.Trim().ToLowerInvariant();

    private static PlaybackSegmentDto ToSegmentDto(MediaEngine.Domain.Entities.PlaybackSegment segment) => new()
    {
        Id = segment.Id,
        AssetId = segment.AssetId,
        Kind = segment.Kind,
        StartSeconds = segment.StartSeconds,
        EndSeconds = segment.EndSeconds,
        Confidence = segment.Confidence,
        Source = segment.Source,
        PluginId = segment.PluginId,
        IsSkippable = segment.IsSkippable,
        ReviewStatus = segment.ReviewStatus,
    };

    private static PlaybackProfileDto ProfileFor(string client) => client switch
    {
        "android" => new PlaybackProfileDto
        {
            Key = "android",
            DisplayName = "Android",
            PreferredDelivery = PlaybackDeliveryModes.DirectStream,
            SupportedContainers = ["mp4", "m4v", "webm", "mp3", "m4a", "aac", "ogg", "wav", "epub", "pdf", "cbz"],
            SupportedVideoCodecs = ["h264", "hevc", "vp8", "vp9", "av1"],
            SupportedAudioCodecs = ["aac", "mp3", "opus", "vorbis", "flac", "pcm_s16le"],
            SupportedSubtitleFormats = ["vtt", "srt"],
            SupportsPlaybackSpeed = true,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
            SupportsOfflineDownloads = true,
        },
        "android-tv" => new PlaybackProfileDto
        {
            Key = "android-tv",
            DisplayName = "Android TV",
            PreferredDelivery = PlaybackDeliveryModes.Hls,
            SupportedContainers = ["mp4", "m4v", "webm", "mp3", "m4a", "aac", "ogg", "wav"],
            SupportedVideoCodecs = ["h264", "hevc", "vp9", "av1"],
            SupportedAudioCodecs = ["aac", "mp3", "opus", "vorbis", "flac"],
            SupportedSubtitleFormats = ["vtt", "srt"],
            MaxHeight = 2160,
            SupportsPlaybackSpeed = true,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
        },
        "apple-tv" => new PlaybackProfileDto
        {
            Key = "apple-tv",
            DisplayName = "Apple TV",
            PreferredDelivery = PlaybackDeliveryModes.Hls,
            SupportedContainers = ["mp4", "m4v", "mp3", "m4a", "aac"],
            SupportedVideoCodecs = ["h264", "hevc"],
            SupportedAudioCodecs = ["aac", "mp3", "alac"],
            SupportedSubtitleFormats = ["vtt"],
            MaxHeight = 2160,
            SupportsPlaybackSpeed = true,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
        },
        "roku" => new PlaybackProfileDto
        {
            Key = "roku",
            DisplayName = "Roku",
            PreferredDelivery = PlaybackDeliveryModes.Hls,
            SupportedContainers = ["mp4", "m4v", "mp3", "m4a", "aac"],
            SupportedVideoCodecs = ["h264", "hevc"],
            SupportedAudioCodecs = ["aac", "mp3"],
            SupportedSubtitleFormats = ["vtt", "srt"],
            MaxHeight = 2160,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
        },
        "mobile-download" => new PlaybackProfileDto
        {
            Key = "mobile-download",
            DisplayName = "Mobile Download",
            PreferredDelivery = PlaybackDeliveryModes.OfflineVariant,
            SupportedContainers = ["mp4", "m4v", "mp3", "m4a", "aac", "epub", "pdf", "cbz"],
            SupportedVideoCodecs = ["h264"],
            SupportedAudioCodecs = ["aac", "mp3"],
            SupportedSubtitleFormats = ["vtt"],
            MaxHeight = 720,
            SupportsPlaybackSpeed = true,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
            SupportsOfflineDownloads = true,
        },
        _ => new PlaybackProfileDto
        {
            Key = "web",
            DisplayName = "Web",
            PreferredDelivery = PlaybackDeliveryModes.DirectStream,
            SupportedContainers = ["mp4", "m4v", "webm", "mp3", "m4a", "aac", "ogg", "wav", "epub", "pdf", "cbz"],
            SupportedVideoCodecs = ["h264", "vp8", "vp9", "av1"],
            SupportedAudioCodecs = ["aac", "mp3", "opus", "vorbis", "flac", "pcm_s16le"],
            SupportedSubtitleFormats = ["vtt"],
            SupportsPlaybackSpeed = true,
            SupportsAlternateAudio = true,
            SupportsSubtitles = true,
            SupportsOfflineDownloads = true,
        },
    };

    private sealed record ResumeRow(double ProgressPct, string? LastAccessed);
}
