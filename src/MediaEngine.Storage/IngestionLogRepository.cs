using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IIngestionLogRepository"/>.
/// Tracks each file through the ingestion pipeline from detection to completion.
/// </summary>
public sealed class IngestionLogRepository : IIngestionLogRepository
{
    private readonly IDatabaseConnection _db;

    public IngestionLogRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task InsertAsync(IngestionLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO ingestion_log
                (id, file_path, content_hash, status, media_type, confidence_score,
                 detected_title, normalized_title, wikidata_qid, error_detail,
                 ingestion_run_id, created_at, updated_at)
            VALUES
                (@id, @path, @hash, @status, @mediaType, @confidence,
                 @title, @normalized, @qid, @error,
                 @runId, @created, @updated);
            """;

        cmd.Parameters.AddWithValue("@id",         entry.Id.ToString());
        cmd.Parameters.AddWithValue("@path",       entry.FilePath);
        cmd.Parameters.AddWithValue("@hash",       (object?)entry.ContentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status",     entry.Status);
        cmd.Parameters.AddWithValue("@mediaType",  (object?)entry.MediaType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence", entry.ConfidenceScore.HasValue
                                                        ? entry.ConfidenceScore.Value
                                                        : DBNull.Value);
        cmd.Parameters.AddWithValue("@title",      (object?)entry.DetectedTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@normalized", (object?)entry.NormalizedTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@qid",        (object?)entry.WikidataQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error",      (object?)entry.ErrorDetail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@runId",      entry.IngestionRunId.HasValue
                                                        ? entry.IngestionRunId.Value.ToString()
                                                        : DBNull.Value);
        cmd.Parameters.AddWithValue("@created",    entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@updated",    entry.UpdatedAt.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateStatusAsync(
        Guid id,
        string status,
        string? contentHash = null,
        string? mediaType = null,
        double? confidenceScore = null,
        string? detectedTitle = null,
        string? normalizedTitle = null,
        string? wikidataQid = null,
        string? errorDetail = null,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        // Build SET clause dynamically for non-null optional fields.
        var setClauses = new List<string> { "status = @status", "updated_at = @updated" };
        cmd.Parameters.AddWithValue("@id",      id.ToString());
        cmd.Parameters.AddWithValue("@status",  status);
        cmd.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));

        if (contentHash is not null)
        {
            setClauses.Add("content_hash = @hash");
            cmd.Parameters.AddWithValue("@hash", contentHash);
        }
        if (mediaType is not null)
        {
            setClauses.Add("media_type = @mediaType");
            cmd.Parameters.AddWithValue("@mediaType", mediaType);
        }
        if (confidenceScore.HasValue)
        {
            setClauses.Add("confidence_score = @confidence");
            cmd.Parameters.AddWithValue("@confidence", confidenceScore.Value);
        }
        if (detectedTitle is not null)
        {
            setClauses.Add("detected_title = @title");
            cmd.Parameters.AddWithValue("@title", detectedTitle);
        }
        if (normalizedTitle is not null)
        {
            setClauses.Add("normalized_title = @normalized");
            cmd.Parameters.AddWithValue("@normalized", normalizedTitle);
        }
        if (wikidataQid is not null)
        {
            setClauses.Add("wikidata_qid = @qid");
            cmd.Parameters.AddWithValue("@qid", wikidataQid);
        }
        if (errorDetail is not null)
        {
            setClauses.Add("error_detail = @error");
            cmd.Parameters.AddWithValue("@error", errorDetail);
        }

        cmd.CommandText = $"UPDATE ingestion_log SET {string.Join(", ", setClauses)} WHERE id = @id;";
        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IngestionLogEntry>> GetRecentAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ingestion_log ORDER BY created_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        return Task.FromResult(ReadEntries(cmd));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IngestionLogEntry>> GetByRunIdAsync(
        Guid runId,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ingestion_log WHERE ingestion_run_id = @runId ORDER BY created_at;";
        cmd.Parameters.AddWithValue("@runId", runId.ToString());

        return Task.FromResult(ReadEntries(cmd));
    }

    /// <inheritdoc/>
    public Task<IngestionLogEntry?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM ingestion_log WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<IngestionLogEntry?>(null);

        return Task.FromResult<IngestionLogEntry?>(MapEntry(reader));
    }

    private static IReadOnlyList<IngestionLogEntry> ReadEntries(SqliteCommand cmd)
    {
        var entries = new List<IngestionLogEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add(MapEntry(reader));
        return entries;
    }

    private static IngestionLogEntry MapEntry(SqliteDataReader reader)
    {
        return new IngestionLogEntry
        {
            Id              = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            FilePath        = reader.GetString(reader.GetOrdinal("file_path")),
            ContentHash     = reader.IsDBNull(reader.GetOrdinal("content_hash"))
                                ? null : reader.GetString(reader.GetOrdinal("content_hash")),
            Status          = reader.GetString(reader.GetOrdinal("status")),
            MediaType       = reader.IsDBNull(reader.GetOrdinal("media_type"))
                                ? null : reader.GetString(reader.GetOrdinal("media_type")),
            ConfidenceScore = reader.IsDBNull(reader.GetOrdinal("confidence_score"))
                                ? null : reader.GetDouble(reader.GetOrdinal("confidence_score")),
            DetectedTitle   = reader.IsDBNull(reader.GetOrdinal("detected_title"))
                                ? null : reader.GetString(reader.GetOrdinal("detected_title")),
            NormalizedTitle = reader.IsDBNull(reader.GetOrdinal("normalized_title"))
                                ? null : reader.GetString(reader.GetOrdinal("normalized_title")),
            WikidataQid     = reader.IsDBNull(reader.GetOrdinal("wikidata_qid"))
                                ? null : reader.GetString(reader.GetOrdinal("wikidata_qid")),
            ErrorDetail     = reader.IsDBNull(reader.GetOrdinal("error_detail"))
                                ? null : reader.GetString(reader.GetOrdinal("error_detail")),
            IngestionRunId  = reader.IsDBNull(reader.GetOrdinal("ingestion_run_id"))
                                ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("ingestion_run_id"))),
            CreatedAt       = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt       = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
        };
    }
}
