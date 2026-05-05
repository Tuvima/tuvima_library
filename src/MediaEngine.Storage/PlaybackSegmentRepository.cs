using System.Globalization;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

public sealed class PlaybackSegmentRepository : IPlaybackSegmentRepository
{
    private readonly IDatabaseConnection _db;

    public PlaybackSegmentRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlaybackSegment>> ListByAssetAsync(Guid assetId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, asset_id, kind, start_seconds, end_seconds, confidence, source,
                   plugin_id, is_skippable, review_status, created_at, updated_at
            FROM playback_segments
            WHERE asset_id = @assetId
              AND review_status <> 'hidden'
            ORDER BY start_seconds, kind;
            """;
        cmd.Parameters.AddWithValue("@assetId", assetId.ToString());

        var results = new List<PlaybackSegment>();
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            results.Add(ReadSegment(reader));

        return results;
    }

    public async Task<PlaybackSegment?> FindByIdAsync(Guid segmentId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, asset_id, kind, start_seconds, end_seconds, confidence, source,
                   plugin_id, is_skippable, review_status, created_at, updated_at
            FROM playback_segments
            WHERE id = @id
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@id", segmentId.ToString());

        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? ReadSegment(reader) : null;
    }

    public async Task UpsertBatchAsync(Guid assetId, IReadOnlyList<PlaybackSegment> segments, CancellationToken ct = default)
    {
        if (segments.Count == 0) return;

        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        foreach (var segment in MergeCandidates(assetId, segments))
        {
            ct.ThrowIfCancellationRequested();
            var id = segment.Id == Guid.Empty ? Guid.NewGuid() : segment.Id;
            var createdAt = segment.CreatedAt == default ? now : segment.CreatedAt;

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO playback_segments
                    (id, asset_id, kind, start_seconds, end_seconds, confidence, source,
                     plugin_id, is_skippable, review_status, created_at, updated_at)
                VALUES
                    (@id, @assetId, @kind, @start, @end, @confidence, @source,
                     @pluginId, @isSkippable, @reviewStatus, @createdAt, @updatedAt)
                ON CONFLICT(id) DO UPDATE SET
                    kind = excluded.kind,
                    start_seconds = excluded.start_seconds,
                    end_seconds = excluded.end_seconds,
                    confidence = excluded.confidence,
                    source = excluded.source,
                    plugin_id = excluded.plugin_id,
                    is_skippable = excluded.is_skippable,
                    review_status = excluded.review_status,
                    updated_at = excluded.updated_at;
                """;
            AddSegmentParameters(cmd, id, assetId, segment, createdAt, now);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        tx.Commit();
    }

    public async Task UpdateAsync(PlaybackSegment segment, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE playback_segments
            SET kind = @kind,
                start_seconds = @start,
                end_seconds = @end,
                confidence = @confidence,
                source = @source,
                plugin_id = @pluginId,
                is_skippable = @isSkippable,
                review_status = @reviewStatus,
                updated_at = @updatedAt
            WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", segment.Id.ToString());
        cmd.Parameters.AddWithValue("@kind", segment.Kind);
        cmd.Parameters.AddWithValue("@start", segment.StartSeconds);
        cmd.Parameters.AddWithValue("@end", (object?)segment.EndSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", Math.Clamp(segment.Confidence, 0, 1));
        cmd.Parameters.AddWithValue("@source", segment.Source);
        cmd.Parameters.AddWithValue("@pluginId", (object?)segment.PluginId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isSkippable", segment.IsSkippable ? 1 : 0);
        cmd.Parameters.AddWithValue("@reviewStatus", segment.ReviewStatus);
        cmd.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid segmentId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM playback_segments WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", segmentId.ToString());
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void AddSegmentParameters(
        Microsoft.Data.Sqlite.SqliteCommand cmd,
        Guid id,
        Guid assetId,
        PlaybackSegment segment,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@assetId", assetId.ToString());
        cmd.Parameters.AddWithValue("@kind", segment.Kind);
        cmd.Parameters.AddWithValue("@start", segment.StartSeconds);
        cmd.Parameters.AddWithValue("@end", (object?)segment.EndSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", Math.Clamp(segment.Confidence, 0, 1));
        cmd.Parameters.AddWithValue("@source", segment.Source);
        cmd.Parameters.AddWithValue("@pluginId", (object?)segment.PluginId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@isSkippable", segment.IsSkippable ? 1 : 0);
        cmd.Parameters.AddWithValue("@reviewStatus", string.IsNullOrWhiteSpace(segment.ReviewStatus) ? "detected" : segment.ReviewStatus);
        cmd.Parameters.AddWithValue("@createdAt", createdAt.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@updatedAt", updatedAt.ToString("O", CultureInfo.InvariantCulture));
    }

    private static IEnumerable<PlaybackSegment> MergeCandidates(Guid assetId, IReadOnlyList<PlaybackSegment> segments)
    {
        return segments
            .Where(s => !string.IsNullOrWhiteSpace(s.Kind) && s.StartSeconds >= 0)
            .Where(s => !s.EndSeconds.HasValue || s.EndSeconds.Value > s.StartSeconds)
            .OrderByDescending(s => s.Confidence)
            .GroupBy(s => new
            {
                Kind = s.Kind.Trim().ToLowerInvariant(),
                Bucket = Math.Round(s.StartSeconds, 0),
                EndBucket = s.EndSeconds.HasValue ? Math.Round(s.EndSeconds.Value, 0) : -1,
                Source = s.Source.Trim().ToLowerInvariant(),
            })
            .Select(g =>
            {
                var s = g.First();
                s.AssetId = assetId;
                s.Kind = s.Kind.Trim().ToLowerInvariant();
                return s;
            });
    }

    private static PlaybackSegment ReadSegment(System.Data.IDataRecord reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        AssetId = Guid.Parse(reader.GetString(1)),
        Kind = reader.GetString(2),
        StartSeconds = reader.GetDouble(3),
        EndSeconds = reader.IsDBNull(4) ? null : reader.GetDouble(4),
        Confidence = reader.GetDouble(5),
        Source = reader.GetString(6),
        PluginId = reader.IsDBNull(7) ? null : reader.GetString(7),
        IsSkippable = reader.GetInt32(8) == 1,
        ReviewStatus = reader.GetString(9),
        CreatedAt = ParseDate(reader.GetString(10)),
        UpdatedAt = ParseDate(reader.GetString(11)),
    };

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
}
