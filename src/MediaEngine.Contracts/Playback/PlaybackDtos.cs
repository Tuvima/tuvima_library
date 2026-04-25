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
