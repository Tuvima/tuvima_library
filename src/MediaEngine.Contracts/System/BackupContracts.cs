using System.Text.Json.Serialization;

namespace MediaEngine.Contracts.System;

public sealed record BackupArchiveDto(
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("size_bytes")] long SizeBytes,
    [property: JsonPropertyName("includes_secrets")] bool IncludesSecrets = false);

public sealed record CreateBackupRequest;

public sealed record ScheduleRestoreRequest(
    [property: JsonPropertyName("file_name")] string FileName);

public sealed record RestoreValidationResultDto(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("file_name")] string FileName,
    [property: JsonPropertyName("validated_at")] DateTimeOffset ValidatedAt,
    [property: JsonPropertyName("message")] string Message);

public sealed record ScheduleRestoreResultDto(
    [property: JsonPropertyName("scheduled")] bool Scheduled,
    [property: JsonPropertyName("restart_required")] bool RestartRequired,
    [property: JsonPropertyName("message")] string Message);
