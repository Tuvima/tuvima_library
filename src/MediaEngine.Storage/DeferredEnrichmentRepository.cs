using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IDeferredEnrichmentRepository"/>.
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
        conn.Execute("""
            INSERT OR IGNORE INTO deferred_enrichment_queue
                (id, entity_id, wikidata_qid, media_type, hints_json,
                 created_at, status, processed_at)
            VALUES
                (@Id, @EntityId, @WikidataQid, @MediaType, @HintsJson,
                 @CreatedAt, @Status, @ProcessedAt);
            """,
            new
            {
                Id          = request.Id,
                EntityId    = request.EntityId,
                request.WikidataQid,
                MediaType   = request.MediaType.ToString(),
                request.HintsJson,
                CreatedAt   = request.CreatedAt,
                request.Status,
                ProcessedAt = request.ProcessedAt,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DeferredEnrichmentRequest>> GetPendingAsync(
        int limit = 50, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<DeferredEnrichmentRow>("""
            SELECT id           AS Id,
                   entity_id    AS EntityId,
                   wikidata_qid AS WikidataQid,
                   media_type   AS MediaType,
                   hints_json   AS HintsJson,
                   created_at   AS CreatedAt,
                   status       AS Status,
                   processed_at AS ProcessedAt
            FROM   deferred_enrichment_queue
            WHERE  status = 'Pending'
            ORDER BY created_at ASC
            LIMIT  @limit;
            """, new { limit }).AsList();

        return Task.FromResult<IReadOnlyList<DeferredEnrichmentRequest>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DeferredEnrichmentRequest>> GetStaleAsync(
        TimeSpan threshold, int limit = 50, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(threshold);

        using var conn = _db.CreateConnection();
        var rows = conn.Query<DeferredEnrichmentRow>("""
            SELECT id           AS Id,
                   entity_id    AS EntityId,
                   wikidata_qid AS WikidataQid,
                   media_type   AS MediaType,
                   hints_json   AS HintsJson,
                   created_at   AS CreatedAt,
                   status       AS Status,
                   processed_at AS ProcessedAt
            FROM   deferred_enrichment_queue
            WHERE  status = 'Pending'
              AND  created_at < @cutoff
            ORDER BY created_at ASC
            LIMIT  @limit;
            """, new { cutoff, limit }).AsList();

        return Task.FromResult<IReadOnlyList<DeferredEnrichmentRequest>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public Task MarkProcessedAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE deferred_enrichment_queue
            SET    status       = 'Processed',
                   processed_at = @now
            WHERE  id = @id;
            """,
            new
            {
                id,
                now = DateTimeOffset.UtcNow,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task MarkProcessedByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE deferred_enrichment_queue
            SET    status       = 'Processed',
                   processed_at = @now
            WHERE  entity_id = @entityId
              AND  status     = 'Pending';
            """,
            new
            {
                entityId,
                now = DateTimeOffset.UtcNow,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> CountPendingAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM deferred_enrichment_queue WHERE status = 'Pending';");
        return Task.FromResult(count);
    }

    // ── Private intermediate row type and mapper ──────────────────────────────

    /// <summary>
    /// Intermediate row type for Dapper mapping.
    /// <see cref="MediaType"/> is stored as TEXT and parsed on read.
    /// <see cref="CreatedAt"/> and <see cref="ProcessedAt"/> use the
    /// registered <c>DateTimeOffsetTypeHandler</c>.
    /// </summary>
    private sealed class DeferredEnrichmentRow
    {
        public Guid           Id          { get; set; }
        public Guid           EntityId    { get; set; }
        public string?        WikidataQid { get; set; }
        public string         MediaType   { get; set; } = string.Empty;
        public string?        HintsJson   { get; set; }
        public DateTimeOffset CreatedAt   { get; set; }
        public string         Status      { get; set; } = string.Empty;
        public DateTimeOffset? ProcessedAt { get; set; }
    }

    private static DeferredEnrichmentRequest MapRow(DeferredEnrichmentRow r) => new()
    {
        Id          = r.Id,
        EntityId    = r.EntityId,
        WikidataQid = r.WikidataQid,
        MediaType   = Enum.TryParse<MediaType>(r.MediaType, true, out var mt) ? mt : MediaType.Unknown,
        HintsJson   = r.HintsJson,
        CreatedAt   = r.CreatedAt,
        Status      = r.Status,
        ProcessedAt = r.ProcessedAt,
    };
}
