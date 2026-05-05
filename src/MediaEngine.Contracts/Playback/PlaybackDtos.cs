namespace MediaEngine.Contracts.Playback;

public sealed record PlaybackManifestDto
{
    public Guid AssetId { get; init; }
    public string Client { get; init; } = "web";
    public string MediaType { get; init; } = "Unknown";
    public string SourceExtension { get; init; } = string.Empty;
    public string RecommendedDelivery { get; init; } = PlaybackDeliveryModes.DirectStream;
    public bool DirectPlaySupported { get; init; }
    public string? DirectStreamUrl { get; init; }
    public string? HlsUrl { get; init; }
    public PlaybackProfileDto Profile { get; init; } = new();
    public IReadOnlyList<PlaybackTrackDto> AudioTracks { get; init; } = [];
    public IReadOnlyList<PlaybackSubtitleTrackDto> SubtitleTracks { get; init; } = [];
    public IReadOnlyList<PlaybackChapterDto> Chapters { get; init; } = [];
    public IReadOnlyList<OfflineVariantDto> OfflineVariants { get; init; } = [];
    public PlaybackResumeDto? Resume { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string? ConversionReason { get; init; }
}

public sealed record ReaderManifestDto
{
    public Guid AssetId { get; init; }
    public string Client { get; init; } = "web";
    public string MediaType { get; init; } = "Books";
    public string ReaderKind { get; init; } = "epub";
    public string? ResourceBaseUrl { get; init; }
    public int? PageCount { get; init; }
    public IReadOnlyList<PlaybackChapterDto> Chapters { get; init; } = [];
    public PlaybackResumeDto? Resume { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record PlaybackProfileDto
{
    public string Key { get; init; } = "web";
    public string DisplayName { get; init; } = "Web";
    public string PreferredDelivery { get; init; } = PlaybackDeliveryModes.DirectStream;
    public IReadOnlyList<string> SupportedContainers { get; init; } = [];
    public IReadOnlyList<string> SupportedVideoCodecs { get; init; } = [];
    public IReadOnlyList<string> SupportedAudioCodecs { get; init; } = [];
    public IReadOnlyList<string> SupportedSubtitleFormats { get; init; } = [];
    public int? MaxHeight { get; init; }
    public int? MaxBitrateKbps { get; init; }
    public bool SupportsPlaybackSpeed { get; init; }
    public bool SupportsAlternateAudio { get; init; }
    public bool SupportsSubtitles { get; init; }
    public bool SupportsOfflineDownloads { get; init; }
}

public sealed record PlaybackTrackDto
{
    public int Index { get; init; }
    public string Kind { get; init; } = "audio";
    public string? Language { get; init; }
    public string? Codec { get; init; }
    public string? DisplayName { get; init; }
    public bool IsDefault { get; init; }
    public int? Channels { get; init; }
    public int? BitrateKbps { get; init; }
}

public sealed record PlaybackSubtitleTrackDto
{
    public int Index { get; init; }
    public string? Language { get; init; }
    public string? Codec { get; init; }
    public string? DisplayName { get; init; }
    public bool IsDefault { get; init; }
    public bool IsForced { get; init; }
    public string? DeliveryUrl { get; init; }
}

public sealed record PlaybackChapterDto
{
    public int Index { get; init; }
    public string Title { get; init; } = string.Empty;
    public double StartSeconds { get; init; }
    public double? EndSeconds { get; init; }
}

public sealed record PlaybackResumeDto
{
    public double ProgressPct { get; init; }
    public double? PositionSeconds { get; init; }
    public DateTimeOffset? LastAccessed { get; init; }
}

public sealed record EncodeJobDto
{
    public Guid Id { get; init; }
    public Guid AssetId { get; init; }
    public string ProfileKey { get; init; } = string.Empty;
    public string Status { get; init; } = EncodeJobStatuses.Queued;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ScheduledFor { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public double ProgressPct { get; init; }
    public string? OutputPath { get; init; }
    public long? OutputBytes { get; init; }
    public string? LastError { get; init; }
    public int RetryCount { get; init; }
}

public sealed record OfflineVariantDto
{
    public Guid Id { get; init; }
    public Guid AssetId { get; init; }
    public string ProfileKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = OfflineVariantStatuses.Pending;
    public string? DownloadUrl { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? Container { get; init; }
    public string? VideoCodec { get; init; }
    public string? AudioCodec { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public int? BitrateKbps { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? SourceHash { get; init; }
}

public sealed record QueueEncodeRequestDto
{
    public string ProfileKey { get; init; } = "mobile-standard";
    public DateTimeOffset? ScheduledFor { get; init; }
}

public sealed record PlaybackDiagnosticsDto
{
    public bool FFmpegAvailable { get; init; }
    public string? FFmpegVersion { get; init; }
    public bool MediaInfoAvailable { get; init; }
    public string? MediaInfoVersion { get; init; }
    public IReadOnlyList<EncodeJobDto> ActiveJobs { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class UserPlaybackSettingsDto
{
    public Guid ProfileId { get; set; }
    public PlaybackGeneralSettingsDto General { get; set; } = new();
    public WatchingSettingsDto Watching { get; set; } = new();
    public ListeningSettingsDto Listening { get; set; } = new();
    public ReadingSettingsDto Reading { get; set; } = new();
    public SubtitleLanguageSettingsDto Subtitles { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }

    public static UserPlaybackSettingsDto CreateDefaults(Guid profileId) => new()
    {
        ProfileId = profileId,
        UpdatedAt = DateTimeOffset.UtcNow,
    };
}

public sealed class PlaybackGeneralSettingsDto
{
    public bool ResumePlayback { get; set; } = true;
    public bool AskBeforeResuming { get; set; }
    public int MarkCompleteThresholdPercent { get; set; } = 90;
    public bool TrackPartiallyPlayedItems { get; set; } = true;
    public bool SyncProgressBetweenBookAndAudiobook { get; set; } = true;
    public bool SpoilerSafeProgress { get; set; } = true;
    public bool UseBannerArtWhenAvailable { get; set; } = true;
}

public sealed class WatchingSettingsDto
{
    public decimal DefaultPlaybackSpeed { get; set; } = 1.0m;
    public int SkipBackSeconds { get; set; } = 10;
    public int SkipForwardSeconds { get; set; } = 30;
    public bool AutoplayNextEpisode { get; set; } = true;
    public bool ContinueFromCredits { get; set; }
    public bool PreferHeroBanners { get; set; } = true;
    public bool RememberPerTitlePlaybackSpeed { get; set; } = true;
    public string PreferredVideoQuality { get; set; } = PlaybackPreferenceValues.Auto;
}

public sealed class ListeningSettingsDto
{
    public decimal AudiobookDefaultSpeed { get; set; } = 1.25m;
    public int SkipBackSeconds { get; set; } = 15;
    public int SkipForwardSeconds { get; set; } = 30;
    public bool MusicCrossfade { get; set; }
    public int CrossfadeSeconds { get; set; } = 5;
    public string DefaultSleepTimer { get; set; } = "30";
    public bool SkipSilence { get; set; } = true;
    public bool RememberPerTitlePlaybackSpeed { get; set; } = true;
    public string OutputPreference { get; set; } = PlaybackPreferenceValues.Auto;
}

public sealed class ReadingSettingsDto
{
    public string ReadingMode { get; set; } = PlaybackPreferenceValues.Paginated;
    public int FontSizePercent { get; set; } = 110;
    public string Theme { get; set; } = PlaybackPreferenceValues.Sepia;
    public string LineSpacing { get; set; } = PlaybackPreferenceValues.Comfortable;
    public string Margins { get; set; } = PlaybackPreferenceValues.Medium;
    public bool KeepScreenAwake { get; set; } = true;
    public bool ShowChapterProgress { get; set; } = true;
    public bool ShowPageNumbers { get; set; } = true;
    public string DefaultComicMode { get; set; } = PlaybackPreferenceValues.Page;
}

public sealed class SubtitleLanguageSettingsDto
{
    public string DefaultSubtitleLanguage { get; set; } = "English";
    public string ForcedSubtitlesMode { get; set; } = PlaybackPreferenceValues.Auto;
    public string AudioLanguage { get; set; } = "English";
    public string SubtitleSize { get; set; } = PlaybackPreferenceValues.Medium;
    public bool SubtitleBackground { get; set; }
    public string SubtitlePosition { get; set; } = PlaybackPreferenceValues.Bottom;
    public string SubtitleStyle { get; set; } = PlaybackPreferenceValues.Clean;
}

public static class PlaybackPreferenceValues
{
    public const string Auto = "Auto";
    public const string Original = "Original";
    public const string High = "High";
    public const string Balanced = "Balanced";
    public const string DataSaver = "DataSaver";
    public const string Headphones = "Headphones";
    public const string Speakers = "Speakers";
    public const string Bluetooth = "Bluetooth";
    public const string Off = "Off";
    public const string EndOfChapter = "EndOfChapter";
    public const string EndOfEpisode = "EndOfEpisode";
    public const string Paginated = "Paginated";
    public const string Scroll = "Scroll";
    public const string Dark = "Dark";
    public const string Sepia = "Sepia";
    public const string Light = "Light";
    public const string System = "System";
    public const string Compact = "Compact";
    public const string Comfortable = "Comfortable";
    public const string Spacious = "Spacious";
    public const string Narrow = "Narrow";
    public const string Medium = "Medium";
    public const string Wide = "Wide";
    public const string Page = "Page";
    public const string Webtoon = "Webtoon";
    public const string DoublePage = "DoublePage";
    public const string FitWidth = "FitWidth";
    public const string Always = "Always";
    public const string Never = "Never";
    public const string Small = "Small";
    public const string Large = "Large";
    public const string ExtraLarge = "ExtraLarge";
    public const string Bottom = "Bottom";
    public const string Top = "Top";
    public const string Clean = "Clean";
    public const string HighContrast = "HighContrast";
    public const string Shadowed = "Shadowed";
}

public static class PlaybackDeliveryModes
{
    public const string DirectStream = "direct-stream";
    public const string Hls = "hls";
    public const string OfflineVariant = "offline-variant";
    public const string Reader = "reader";
}

public static class EncodeJobStatuses
{
    public const string Queued = "queued";
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Complete = "complete";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public static class OfflineVariantStatuses
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Expired = "expired";
    public const string Failed = "failed";
}
