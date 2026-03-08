using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReviewQueueRepository"/>.
///
/// ORM-less: all SQL is executed via <see cref="SqliteCommand"/>.
/// All methods are async-safe and non-blocking.
/// </summary>
public sealed class ReviewQueueRepository : IReviewQueueRepository
{
    private readonly IDatabaseConnection _db;

    public ReviewQueueRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<Guid> InsertAsync(ReviewQueueEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO review_queue
                (id, entity_id, entity_type, trigger, status,
                 proposed_hub_id, confidence_score, candidates_json,
                 detail, created_at, resolved_at, resolved_by)
            VALUES
                (@id, @entityId, @entityType, @trigger, @status,
                 @proposedHubId, @confidence, @candidates,
                 @detail, @createdAt, @resolvedAt, @resolvedBy);
            """;

        cmd.Parameters.AddWithValue("@id",            entry.Id.ToString());
        cmd.Parameters.AddWithValue("@entityId",      entry.EntityId.ToString());
        cmd.Parameters.AddWithValue("@entityType",    entry.EntityType);
        cmd.Parameters.AddWithValue("@trigger",       entry.Trigger);
        cmd.Parameters.AddWithValue("@status",        entry.Status);
        cmd.Parameters.AddWithValue("@proposedHubId", (object?)entry.ProposedHubId    ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@confidence",    entry.ConfidenceScore.HasValue
                                                          ? (object)entry.ConfidenceScore.Value
                                                          : DBNull.Value);
        cmd.Parameters.AddWithValue("@candidates",    (object?)entry.CandidatesJson   ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@detail",        (object?)entry.Detail           ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt",     entry.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@resolvedAt",    entry.ResolvedAt.HasValue
                                                          ? (object)entry.ResolvedAt.Value.ToString("O")
                                                          : DBNull.Value);
        cmd.Parameters.AddWithValue("@resolvedBy",    (object?)entry.ResolvedBy       ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.FromResult(entry.Id);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, entity_type, trigger, status,
                   proposed_hub_id, confidence_score, candidates_json,
                   detail, created_at, resolved_at, resolved_by
            FROM   review_queue
            WHERE  status = @status
            ORDER BY created_at DESC
            LIMIT  @limit;
            """;
        cmd.Parameters.AddWithValue("@status", ReviewStatus.Pending);
        cmd.Parameters.AddWithValue("@limit",  limit);

        var results = new List<ReviewQueueEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(results);
    }

    /// <inheritdoc/>
    public Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, entity_type, trigger, status,
                   proposed_hub_id, confidence_score, candidates_json,
                   detail, created_at, resolved_at, resolved_by
            FROM   review_queue
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return Task.FromResult<ReviewQueueEntry?>(MapRow(reader));
        }

        return Task.FromResult<ReviewQueueEntry?>(null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, entity_type, trigger, status,
                   proposed_hub_id, confidence_score, candidates_json,
                   detail, created_at, resolved_at, resolved_by
            FROM   review_queue
            WHERE  entity_id = @entityId
            ORDER BY created_at DESC;
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());

        var results = new List<ReviewQueueEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(results);
    }

    /// <inheritdoc/>
    public Task UpdateStatusAsync(
        Guid id,
        string status,
        string? resolvedBy = null,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE review_queue
            SET    status      = @status,
                   resolved_at = @resolvedAt,
                   resolved_by = @resolvedBy
            WHERE  id = @id;
            """;

        cmd.Parameters.AddWithValue("@id",         id.ToString());
        cmd.Parameters.AddWithValue("@status",     status);
        cmd.Parameters.AddWithValue("@resolvedAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@resolvedBy", (object?)resolvedBy ?? DBNull.Value);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM review_queue
            WHERE  status = @status;
            """;
        cmd.Parameters.AddWithValue("@status", ReviewStatus.Pending);

        var count = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<int> DismissAllByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE review_queue
            SET    status      = @dismissed,
                   resolved_at = @now,
                   resolved_by = 'reconciliation'
            WHERE  entity_id = @entityId
              AND  status     = @pending;
            """;
        cmd.Parameters.AddWithValue("@dismissed", ReviewStatus.Dismissed);
        cmd.Parameters.AddWithValue("@now",        DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@entityId",   entityId.ToString());
        cmd.Parameters.AddWithValue("@pending",    ReviewStatus.Pending);

        var rows = cmd.ExecuteNonQuery();
        return Task.FromResult(rows);
    }

    // ── Row mapping ─────────────────────────────────────────────────────────────

    private static ReviewQueueEntry MapRow(SqliteDataReader reader)
    {
        var resolvedAtText = reader.IsDBNull(10) ? null : reader.GetString(10);

        return new ReviewQueueEntry
        {
            Id              = Guid.Parse(reader.GetString(0)),
            EntityId        = Guid.Parse(reader.GetString(1)),
            EntityType      = reader.GetString(2),
            Trigger         = reader.GetString(3),
            Status          = reader.GetString(4),
            ProposedHubId   = reader.IsDBNull(5) ? null : reader.GetString(5),
            ConfidenceScore = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            CandidatesJson  = reader.IsDBNull(7) ? null : reader.GetString(7),
            Detail          = reader.IsDBNull(8) ? null : reader.GetString(8),
            CreatedAt       = DateTimeOffset.TryParse(reader.GetString(9), out var created)
                                  ? created : DateTimeOffset.UtcNow,
            ResolvedAt      = resolvedAtText is not null
                                  && DateTimeOffset.TryParse(resolvedAtText, out var resolved)
                                  ? resolved : null,
            ResolvedBy      = reader.IsDBNull(11) ? null : reader.GetString(11),
        };
    }
}
