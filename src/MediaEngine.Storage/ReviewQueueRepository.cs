using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IReviewQueueRepository"/>.
///
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
        conn.Execute("""
            INSERT INTO review_queue
                (id, entity_id, entity_type, trigger, status,
                 proposed_hub_id, confidence_score, candidates_json,
                 detail, created_at, resolved_at, resolved_by)
            VALUES
                (@id, @entityId, @entityType, @trigger, @status,
                 @proposedHubId, @confidence, @candidates,
                 @detail, @createdAt, @resolvedAt, @resolvedBy)
            """, new
        {
            id            = entry.Id.ToString(),
            entityId      = entry.EntityId.ToString(),
            entityType    = entry.EntityType,
            trigger       = entry.Trigger,
            status        = entry.Status,
            proposedHubId = entry.ProposedHubId,
            confidence    = entry.ConfidenceScore,
            candidates    = entry.CandidatesJson,
            detail        = entry.Detail,
            createdAt     = entry.CreatedAt.ToString("O"),
            resolvedAt    = entry.ResolvedAt.HasValue ? (object)entry.ResolvedAt.Value.ToString("O") : null,
            resolvedBy    = entry.ResolvedBy,
        });

        return Task.FromResult(entry.Id);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ReviewQueueRow>("""
            SELECT id, entity_id AS EntityId, entity_type AS EntityType, trigger, status,
                   proposed_hub_id AS ProposedHubId, confidence_score AS ConfidenceScore,
                   candidates_json AS CandidatesJson, detail, created_at AS CreatedAt,
                   resolved_at AS ResolvedAt, resolved_by AS ResolvedBy
            FROM   review_queue
            WHERE  status = @status
            ORDER BY created_at DESC
            LIMIT  @limit
            """, new { status = ReviewStatus.Pending, limit }).AsList();

        return Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(rows.Select(MapRow).ToList());
    }

    /// <inheritdoc/>
    public Task<ReviewQueueEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<ReviewQueueRow>("""
            SELECT id, entity_id AS EntityId, entity_type AS EntityType, trigger, status,
                   proposed_hub_id AS ProposedHubId, confidence_score AS ConfidenceScore,
                   candidates_json AS CandidatesJson, detail, created_at AS CreatedAt,
                   resolved_at AS ResolvedAt, resolved_by AS ResolvedBy
            FROM   review_queue
            WHERE  id = @id
            """, new { id = id.ToString() });

        return Task.FromResult(row is null ? null : (ReviewQueueEntry?)MapRow(row));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ReviewQueueRow>("""
            SELECT id, entity_id AS EntityId, entity_type AS EntityType, trigger, status,
                   proposed_hub_id AS ProposedHubId, confidence_score AS ConfidenceScore,
                   candidates_json AS CandidatesJson, detail, created_at AS CreatedAt,
                   resolved_at AS ResolvedAt, resolved_by AS ResolvedBy
            FROM   review_queue
            WHERE  entity_id = @entityId
            ORDER BY created_at DESC
            """, new { entityId = entityId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(rows.Select(MapRow).ToList());
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReviewQueueEntry>> GetPendingByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<ReviewQueueRow>("""
            SELECT id, entity_id AS EntityId, entity_type AS EntityType, trigger, status,
                   proposed_hub_id AS ProposedHubId, confidence_score AS ConfidenceScore,
                   candidates_json AS CandidatesJson, detail, created_at AS CreatedAt,
                   resolved_at AS ResolvedAt, resolved_by AS ResolvedBy
            FROM   review_queue
            WHERE  entity_id = @entityId AND status = @status
            ORDER BY created_at DESC
            """, new { entityId = entityId.ToString(), status = ReviewStatus.Pending }).AsList();

        return Task.FromResult<IReadOnlyList<ReviewQueueEntry>>(rows.Select(MapRow).ToList());
    }

    /// <inheritdoc/>
    public Task UpdateStatusAsync(
        Guid id,
        string status,
        string? resolvedBy = null,
        CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE review_queue
            SET    status      = @status,
                   resolved_at = @resolvedAt,
                   resolved_by = @resolvedBy
            WHERE  id = @id
            """, new
        {
            id         = id.ToString(),
            status,
            resolvedAt = DateTimeOffset.UtcNow.ToString("O"),
            resolvedBy,
        });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>(
            "SELECT COUNT(DISTINCT entity_id) FROM review_queue WHERE status = @status",
            new { status = ReviewStatus.Pending });

        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task<int> DismissAllByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Execute("""
            UPDATE review_queue
            SET    status      = @dismissed,
                   resolved_at = @now,
                   resolved_by = 'reconciliation'
            WHERE  entity_id = @entityId
              AND  status     = @pending
            """, new
        {
            dismissed = ReviewStatus.Dismissed,
            now       = DateTimeOffset.UtcNow.ToString("O"),
            entityId  = entityId.ToString(),
            pending   = ReviewStatus.Pending,
        });

        return Task.FromResult(rows);
    }

    /// <inheritdoc/>
    public Task<int> ResolveAllByEntityAsync(Guid entityId, string resolvedBy = "system:auto-organize", CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Execute("""
            UPDATE review_queue
            SET    status      = @resolved,
                   resolved_at = @now,
                   resolved_by = @resolvedBy
            WHERE  entity_id = @entityId
              AND  status     = @pending
            """, new
        {
            resolved   = ReviewStatus.Resolved,
            now        = DateTimeOffset.UtcNow.ToString("O"),
            resolvedBy,
            entityId   = entityId.ToString(),
            pending    = ReviewStatus.Pending,
        });

        return Task.FromResult(rows);
    }

    /// <inheritdoc/>
    public Task<int> PurgeOrphanedAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var deleted = conn.Execute(
            "DELETE FROM review_queue WHERE entity_id NOT IN (SELECT id FROM media_assets)");

        return Task.FromResult(deleted);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Flat data-transfer struct that Dapper populates from a SELECT row.</summary>
    private sealed class ReviewQueueRow
    {
        public string Id            { get; set; } = "";
        public string EntityId      { get; set; } = "";
        public string EntityType    { get; set; } = "";
        public string Trigger       { get; set; } = "";
        public string Status        { get; set; } = "";
        public string? ProposedHubId  { get; set; }
        public double? ConfidenceScore { get; set; }
        public string? CandidatesJson  { get; set; }
        public string? Detail          { get; set; }
        public string CreatedAt   { get; set; } = "";
        public string? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
    }

    private static ReviewQueueEntry MapRow(ReviewQueueRow r) => new()
    {
        Id              = Guid.Parse(r.Id),
        EntityId        = Guid.Parse(r.EntityId),
        EntityType      = r.EntityType,
        Trigger         = r.Trigger,
        Status          = r.Status,
        ProposedHubId   = r.ProposedHubId,
        ConfidenceScore = r.ConfidenceScore,
        CandidatesJson  = r.CandidatesJson,
        Detail          = r.Detail,
        CreatedAt       = DateTimeOffset.TryParse(r.CreatedAt, out var created) ? created : DateTimeOffset.UtcNow,
        ResolvedAt      = r.ResolvedAt is not null && DateTimeOffset.TryParse(r.ResolvedAt, out var resolved)
                              ? resolved : null,
        ResolvedBy      = r.ResolvedBy,
    };
}
