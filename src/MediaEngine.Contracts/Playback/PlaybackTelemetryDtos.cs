using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.Playback;

public sealed record PlaybackTelemetrySessionDto
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("player_session_id")] public Guid PlayerSessionId { get; init; }
    [JsonPropertyName("account_id")] public Guid AccountId { get; init; }
    [JsonPropertyName("profile_id")] public Guid ProfileId { get; init; }
    [JsonPropertyName("application_id")] public Guid? ApplicationId { get; init; }
    [JsonPropertyName("device_id")] public Guid? DeviceId { get; init; }
    [JsonPropertyName("asset_id")] public Guid? AssetId { get; init; }
    [JsonPropertyName("library_id")] public Guid LibraryId { get; init; }
    [JsonPropertyName("feature_id")] public string FeatureId { get; init; } = string.Empty;
    [JsonPropertyName("media_type")] public string MediaType { get; init; } = string.Empty;
    [JsonPropertyName("state")] public string State { get; init; } = string.Empty;
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("last_observed_at")] public DateTimeOffset LastObservedAt { get; init; }
    [JsonPropertyName("ended_at")] public DateTimeOffset? EndedAt { get; init; }
    [JsonPropertyName("started_position_seconds")] public double StartedPositionSeconds { get; init; }
    [JsonPropertyName("last_position_seconds")] public double LastPositionSeconds { get; init; }
    [JsonPropertyName("duration_seconds")] public double? DurationSeconds { get; init; }
    [JsonPropertyName("played_duration_seconds")] public double PlayedDurationSeconds { get; init; }
    [JsonPropertyName("completion_reason")] public string? CompletionReason { get; init; }
    [JsonPropertyName("delivery_mode")] public string? DeliveryMode { get; init; }
    [JsonPropertyName("container")] public string? Container { get; init; }
    [JsonPropertyName("video_codec")] public string? VideoCodec { get; init; }
    [JsonPropertyName("audio_codec")] public string? AudioCodec { get; init; }
    [JsonPropertyName("width")] public int? Width { get; init; }
    [JsonPropertyName("height")] public int? Height { get; init; }
    [JsonPropertyName("bitrate_kbps")] public int? BitrateKbps { get; init; }
    [JsonPropertyName("connection_type")] public string? ConnectionType { get; init; }
    [JsonPropertyName("client_name")] public string? ClientName { get; init; }
    [JsonPropertyName("client_version")] public string? ClientVersion { get; init; }
}

public sealed record PlaybackTelemetryPageDto(
    [property: JsonPropertyName("items")] IReadOnlyList<PlaybackTelemetrySessionDto> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record PlaybackAnalyticsDto
{
    [JsonPropertyName("session_count")] public long SessionCount { get; init; }
    [JsonPropertyName("completed_count")] public long CompletedCount { get; init; }
    [JsonPropertyName("played_duration_seconds")] public double PlayedDurationSeconds { get; init; }
    [JsonPropertyName("unique_accounts")] public long UniqueAccounts { get; init; }
    [JsonPropertyName("unique_profiles")] public long UniqueProfiles { get; init; }
    [JsonPropertyName("unique_assets")] public long UniqueAssets { get; init; }
    [JsonPropertyName("direct_play_count")] public long DirectPlayCount { get; init; }
    [JsonPropertyName("remux_count")] public long RemuxCount { get; init; }
    [JsonPropertyName("transcode_count")] public long TranscodeCount { get; init; }
}

public sealed record PlaybackAnalyticsGroupDto
{
    [JsonPropertyName("account_id")] public Guid? AccountId { get; init; }
    [JsonPropertyName("profile_id")] public Guid? ProfileId { get; init; }
    [JsonPropertyName("library_id")] public Guid? LibraryId { get; init; }
    [JsonPropertyName("device_id")] public Guid? DeviceId { get; init; }
    [JsonPropertyName("session_count")] public long SessionCount { get; init; }
    [JsonPropertyName("completed_count")] public long CompletedCount { get; init; }
    [JsonPropertyName("played_duration_seconds")] public double PlayedDurationSeconds { get; init; }
    [JsonPropertyName("last_played_at")] public DateTimeOffset LastPlayedAt { get; init; }
}

public sealed record PlaybackAnalyticsGroupsDto(
    [property: JsonPropertyName("items")] IReadOnlyList<PlaybackAnalyticsGroupDto> Items);
