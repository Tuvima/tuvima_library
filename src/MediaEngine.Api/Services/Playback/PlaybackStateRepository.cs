using System.Globalization;
using Dapper;
using MediaEngine.Contracts.Playback;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Playback;

public sealed class PlaybackStateRepository
{
    private readonly IDatabaseConnection _db;

    public PlaybackStateRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public Task StoreInspectionAsync(
        Guid assetId,
        string sourceHash,
        long? fileSize,
        double? durationSecs,
        string? container,
        string? metadataJson,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO playback_inspection_cache
                (asset_id, source_hash, inspected_at, file_size, duration_secs, container, metadata_json)
            VALUES
                (@assetId, @sourceHash, @inspectedAt, @fileSize, @durationSecs, @container, @metadataJson)
            ON CONFLICT(asset_id, source_hash) DO UPDATE SET
                inspected_at = excluded.inspected_at,
                file_size = excluded.file_size,
                duration_secs = excluded.duration_secs,
                container = excluded.container,
                metadata_json = excluded.metadata_json;
            """,
            new
            {
                assetId = assetId.ToString(),
                sourceHash,
                inspectedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                fileSize,
                durationSecs,
                container,
                metadataJson,
            });

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<OfflineVariantDto>> ListOfflineVariantsAsync(Guid assetId, string sourceHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<OfflineVariantRow>("""
            SELECT id AS Id,
                   asset_id AS AssetId,
                   profile_key AS ProfileKey,
                   source_hash AS SourceHash,
                   display_name AS DisplayName,
                   status AS Status,
                   output_path AS OutputPath,
                   file_size AS FileSizeBytes,
                   container AS Container,
                   video_codec AS VideoCodec,
                   audio_codec AS AudioCodec,
                   width AS Width,
                   height AS Height,
                   bitrate_kbps AS BitrateKbps,
                   created_at AS CreatedAt,
                   expires_at AS ExpiresAt
            FROM offline_variants
            WHERE asset_id = @assetId
              AND source_hash = @sourceHash
            ORDER BY created_at DESC;
            """,
            new { assetId = assetId.ToString(), sourceHash }).AsList();

        return Task.FromResult<IReadOnlyList<OfflineVariantDto>>(rows.Select(ToDto).ToList());
    }

    public Task<OfflineVariantFile?> GetOfflineVariantFileAsync(Guid assetId, Guid variantId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<OfflineVariantFile>("""
            SELECT output_path AS OutputPath,
                   status AS Status,
                   file_size AS FileSizeBytes
            FROM offline_variants
            WHERE id = @variantId
              AND asset_id = @assetId
            LIMIT 1;
            """,
            new { assetId = assetId.ToString(), variantId = variantId.ToString() });

        return Task.FromResult(row);
    }

    public Task<EncodeJobDto> QueueEncodeJobAsync(Guid assetId, string profileKey, string sourceHash, DateTimeOffset? scheduledFor, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();

        var existing = conn.QueryFirstOrDefault<EncodeJobRow>("""
            SELECT id AS Id,
                   asset_id AS AssetId,
                   profile_key AS ProfileKey,
                   status AS Status,
                   created_at AS CreatedAt,
                   scheduled_for AS ScheduledFor,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt,
                   progress_pct AS ProgressPct,
                   output_path AS OutputPath,
                   output_bytes AS OutputBytes,
                   last_error AS LastError,
                   retry_count AS RetryCount
            FROM encode_jobs
            WHERE asset_id = @assetId
              AND profile_key = @profileKey
              AND source_hash = @sourceHash
              AND status IN ('queued', 'scheduled', 'running', 'complete')
            ORDER BY created_at DESC
            LIMIT 1;
            """,
            new { assetId = assetId.ToString(), profileKey, sourceHash });

        if (existing is not null)
        {
            return Task.FromResult(ToDto(existing));
        }

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var status = scheduledFor.HasValue && scheduledFor.Value > now
            ? EncodeJobStatuses.Scheduled
            : EncodeJobStatuses.Queued;

        conn.Execute("""
            INSERT INTO encode_jobs
                (id, asset_id, profile_key, source_hash, status, created_at, scheduled_for, progress_pct)
            VALUES
                (@id, @assetId, @profileKey, @sourceHash, @status, @createdAt, @scheduledFor, 0);
            """,
            new
            {
                id = id.ToString(),
                assetId = assetId.ToString(),
                profileKey,
                sourceHash,
                status,
                createdAt = now.ToString("O", CultureInfo.InvariantCulture),
                scheduledFor = scheduledFor?.ToString("O", CultureInfo.InvariantCulture),
            });

        return Task.FromResult(new EncodeJobDto
        {
            Id = id,
            AssetId = assetId,
            ProfileKey = profileKey,
            Status = status,
            CreatedAt = now,
            ScheduledFor = scheduledFor,
        });
    }

    public Task<IReadOnlyList<EncodeJobDto>> ListEncodeJobsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<EncodeJobRow>("""
            SELECT id AS Id,
                   asset_id AS AssetId,
                   profile_key AS ProfileKey,
                   status AS Status,
                   created_at AS CreatedAt,
                   scheduled_for AS ScheduledFor,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt,
                   progress_pct AS ProgressPct,
                   output_path AS OutputPath,
                   output_bytes AS OutputBytes,
                   last_error AS LastError,
                   retry_count AS RetryCount
            FROM encode_jobs
            ORDER BY created_at DESC
            LIMIT 200;
            """).AsList();

        return Task.FromResult<IReadOnlyList<EncodeJobDto>>(rows.Select(ToDto).ToList());
    }

    public Task<IReadOnlyList<EncodeJobDto>> ListActiveEncodeJobsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = conn.Query<EncodeJobRow>("""
            SELECT id AS Id,
                   asset_id AS AssetId,
                   profile_key AS ProfileKey,
                   status AS Status,
                   created_at AS CreatedAt,
                   scheduled_for AS ScheduledFor,
                   started_at AS StartedAt,
                   completed_at AS CompletedAt,
                   progress_pct AS ProgressPct,
                   output_path AS OutputPath,
                   output_bytes AS OutputBytes,
                   last_error AS LastError,
                   retry_count AS RetryCount
            FROM encode_jobs
            WHERE status IN ('queued', 'scheduled', 'running')
            ORDER BY COALESCE(scheduled_for, created_at), created_at
            LIMIT 50;
            """).AsList();

        return Task.FromResult<IReadOnlyList<EncodeJobDto>>(rows.Select(ToDto).ToList());
    }

    public Task CancelEncodeJobAsync(Guid jobId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE encode_jobs
            SET status = 'cancelled',
                completed_at = @completedAt
            WHERE id = @jobId
              AND status IN ('queued', 'scheduled', 'running');
            """,
            new
            {
                jobId = jobId.ToString(),
                completedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            });

        return Task.CompletedTask;
    }

    private static OfflineVariantDto ToDto(OfflineVariantRow row)
    {
        var id = Guid.Parse(row.Id);
        var assetId = Guid.Parse(row.AssetId);
        return new OfflineVariantDto
        {
            Id = id,
            AssetId = assetId,
            ProfileKey = row.ProfileKey,
            DisplayName = row.DisplayName,
            Status = row.Status,
            DownloadUrl = string.Equals(row.Status, OfflineVariantStatuses.Ready, StringComparison.OrdinalIgnoreCase)
                ? $"/playback/{assetId}/offline/{id}"
                : null,
            FileSizeBytes = row.FileSizeBytes,
            Container = row.Container,
            VideoCodec = row.VideoCodec,
            AudioCodec = row.AudioCodec,
            Width = row.Width,
            Height = row.Height,
            BitrateKbps = row.BitrateKbps,
            CreatedAt = ParseDate(row.CreatedAt) ?? DateTimeOffset.MinValue,
            ExpiresAt = ParseDate(row.ExpiresAt),
            SourceHash = row.SourceHash,
        };
    }

    private static EncodeJobDto ToDto(EncodeJobRow row) => new()
    {
        Id = Guid.Parse(row.Id),
        AssetId = Guid.Parse(row.AssetId),
        ProfileKey = row.ProfileKey,
        Status = row.Status,
        CreatedAt = ParseDate(row.CreatedAt) ?? DateTimeOffset.MinValue,
        ScheduledFor = ParseDate(row.ScheduledFor),
        StartedAt = ParseDate(row.StartedAt),
        CompletedAt = ParseDate(row.CompletedAt),
        ProgressPct = row.ProgressPct,
        OutputPath = row.OutputPath,
        OutputBytes = row.OutputBytes,
        LastError = row.LastError,
        RetryCount = row.RetryCount,
    };

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private sealed record OfflineVariantRow(
        string Id,
        string AssetId,
        string ProfileKey,
        string SourceHash,
        string DisplayName,
        string Status,
        string? OutputPath,
        long? FileSizeBytes,
        string? Container,
        string? VideoCodec,
        string? AudioCodec,
        int? Width,
        int? Height,
        int? BitrateKbps,
        string CreatedAt,
        string? ExpiresAt);

    private sealed record EncodeJobRow(
        string Id,
        string AssetId,
        string ProfileKey,
        string Status,
        string CreatedAt,
        string? ScheduledFor,
        string? StartedAt,
        string? CompletedAt,
        double ProgressPct,
        string? OutputPath,
        long? OutputBytes,
        string? LastError,
        int RetryCount);
}

public sealed record OfflineVariantFile(string? OutputPath, string Status, long? FileSizeBytes);
