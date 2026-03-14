using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IDeferredEnrichmentRepository"/>.
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
/// </summary>
public sealed class DeferredEnrichmentRepository : IDeferredEnrichmentRepository
{
    private readonly IDatabaseConnection _db;

    public DeferredEnrichmentRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task InsertAsync(DeferredEnrichmentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO deferred_enrichment_queue
                (id, entity_id, wikidata_qid, media_type, hints_json,
                 created_at, status, processed_at)
            VALUES
                (@id, @entityId, @qid, @mediaType, @hints,
                 @createdAt, @status, @processedAt);
            """;

        cmd.Parameters.AddWithValue("@id",          request.Id.ToString());
        cmd.Parameters.AddWithValue("@entityId",    request.EntityId.ToString());
        cmd.Parameters.AddWithValue("@qid",         (object?)request.WikidataQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mediaType",   request.MediaType.ToString());
        cmd.Parameters.AddWithValue("@hints",       (object?)request.HintsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt",   request.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@status",      request.Status);
        cmd.Parameters.AddWithValue("@processedAt", request.ProcessedAt.HasValue
                                                        ? (object)request.ProcessedAt.Value.ToString("O")
                                                        : DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DeferredEnrichmentRequest>> GetPendingAsync(
        int limit = 50, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, wikidata_qid, media_type, hints_json,
                   created_at, status, processed_at
            FROM   deferred_enrichment_queue
            WHERE  status = 'Pending'
            ORDER BY created_at ASC
            LIMIT  @limit;
            """;
        cmd.Parameters.AddWithValue("@limit", limit);

        var results = new List<DeferredEnrichmentRequest>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<DeferredEnrichmentRequest>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DeferredEnrichmentRequest>> GetStaleAsync(
        TimeSpan threshold, int limit = 50, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(threshold);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, wikidata_qid, media_type, hints_json,
                   created_at, status, processed_at
            FROM   deferred_enrichment_queue
            WHERE  status = 'Pending'
              AND  created_at < @cutoff
            ORDER BY created_at ASC
            LIMIT  @limit;
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        cmd.Parameters.AddWithValue("@limit",  limit);

        var results = new List<DeferredEnrichmentRequest>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<DeferredEnrichmentRequest>>(results);
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE deferred_enrichment_queue
            SET    status       = 'Processed',
                   processed_at = @now
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",  id.ToString());
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkProcessedByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE deferred_enrichment_queue
            SET    status       = 'Processed',
                   processed_at = @now
            WHERE  entity_id = @entityId
              AND  status     = 'Pending';
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());
        cmd.Parameters.AddWithValue("@now",      DateTimeOffset.UtcNow.ToString("O"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM deferred_enrichment_queue WHERE status = 'Pending';";

        var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return Task.FromResult(count);
    }

    // ── Row mapping ──────────────────────────────────────────────────────────────

    private static DeferredEnrichmentRequest MapRow(SqliteDataReader reader)
    {
        return new DeferredEnrichmentRequest
        {
            Id          = Guid.Parse(reader.GetString(0)),
            EntityId    = Guid.Parse(reader.GetString(1)),
            WikidataQid = reader.IsDBNull(2) ? null : reader.GetString(2),
            MediaType   = Enum.TryParse<MediaType>(reader.GetString(3), true, out var mt)
                              ? mt : MediaType.Unknown,
            HintsJson   = reader.IsDBNull(4) ? null : reader.GetString(4),
            CreatedAt   = DateTimeOffset.TryParse(reader.GetString(5), out var created)
                              ? created : DateTimeOffset.UtcNow,
            Status      = reader.GetString(6),
            ProcessedAt = reader.IsDBNull(7) ? null
                              : DateTimeOffset.TryParse(reader.GetString(7), out var processed)
                                  ? processed : null,
        };
    }
}
