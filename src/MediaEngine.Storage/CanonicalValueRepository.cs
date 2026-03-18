using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="ICanonicalValueRepository"/>.
///
/// Canonical values use a composite primary key (entity_id, key), so each
/// upsert replaces the previous winner for a given field.  The full scoring
/// history is preserved in <c>metadata_claims</c>; only the current winner
/// lives here.
///
/// Spec: Phase 4 – Canonical Integrity invariant;
///       Phase 9 – External Metadata Adapters § Canonical Persistence;
///       Phase B – Conflict Surfacing (B-05).
/// </summary>
public sealed class CanonicalValueRepository : ICanonicalValueRepository
{
    private readonly IDatabaseConnection _db;

    public CanonicalValueRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // ICanonicalValueRepository
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task UpsertBatchAsync(
        IReadOnlyList<CanonicalValue> values,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (values.Count == 0)
            return;

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();

            // Single transaction: atomicity + significant write-performance gain.
            using var tx = conn.BeginTransaction();
            try
            {
                ct.ThrowIfCancellationRequested();

                // INSERT OR REPLACE honours the (entity_id, key) PRIMARY KEY:
                // if a row already exists it is deleted then re-inserted with the
                // new value and timestamp.
                conn.Execute("""
                    INSERT OR REPLACE INTO canonical_values
                        (entity_id, key, value, last_scored_at, is_conflicted, winning_provider_id, needs_review)
                    VALUES
                        (@EntityId, @Key, @Value, @LastScoredAt, @IsConflicted, @WinningProviderId, @NeedsReview);
                    """,
                    values.Select(cv => new
                    {
                        EntityId          = cv.EntityId.ToString(),
                        cv.Key,
                        cv.Value,
                        LastScoredAt      = cv.LastScoredAt.ToString("o"),
                        IsConflicted      = cv.IsConflicted ? 1 : 0,
                        WinningProviderId = cv.WinningProviderId.HasValue
                                               ? cv.WinningProviderId.Value.ToString()
                                               : (string?)null,
                        NeedsReview       = cv.NeedsReview ? 1 : 0,
                    }),
                    transaction: tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CanonicalValue>> GetByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<CanonicalValueRow>("""
            SELECT entity_id           AS EntityId,
                   key                 AS Key,
                   value               AS Value,
                   last_scored_at      AS LastScoredAt,
                   is_conflicted       AS IsConflicted,
                   winning_provider_id AS WinningProviderId,
                   needs_review        AS NeedsReview
            FROM   canonical_values
            WHERE  entity_id = @entityId
            ORDER  BY key ASC;
            """, new { entityId }).AsList();

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<CanonicalValueRow>("""
            SELECT entity_id           AS EntityId,
                   key                 AS Key,
                   value               AS Value,
                   last_scored_at      AS LastScoredAt,
                   is_conflicted       AS IsConflicted,
                   winning_provider_id AS WinningProviderId,
                   needs_review        AS NeedsReview
            FROM   canonical_values
            WHERE  is_conflicted = 1
            ORDER  BY last_scored_at DESC;
            """).AsList();

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public async Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "DELETE FROM canonical_values WHERE entity_id = @entityId;",
                new { entityId });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteByKeyAsync(Guid entityId, string key, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "DELETE FROM canonical_values WHERE entity_id = @entityId AND key = @key;",
                new { entityId, key });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> FindByValueAsync(
        string key,
        string value,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        using var conn = _db.CreateConnection();
        var ids = conn.Query<string>("""
            SELECT entity_id
            FROM   canonical_values
            WHERE  key   = @key   COLLATE NOCASE
              AND  value = @value COLLATE NOCASE;
            """, new { key, value })
            .Select(Guid.Parse)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(ids);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CanonicalValue>> FindByKeyAndPrefixAsync(
        string key,
        string prefix,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        using var conn = _db.CreateConnection();
        var rows = conn.Query<CanonicalValueRow>("""
            SELECT entity_id           AS EntityId,
                   key                 AS Key,
                   value               AS Value,
                   last_scored_at      AS LastScoredAt,
                   is_conflicted       AS IsConflicted,
                   winning_provider_id AS WinningProviderId,
                   needs_review        AS NeedsReview
            FROM   canonical_values
            WHERE  key   = @key   COLLATE NOCASE
              AND  value LIKE @prefix || '%';
            """, new { key, prefix }).AsList();

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(rows.ConvertAll(MapRow));
    }

    // -------------------------------------------------------------------------
    // Private intermediate row type and mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Intermediate row type for Dapper mapping.
    /// <see cref="IsConflicted"/> and <see cref="NeedsReview"/> are integers (0/1) in SQLite;
    /// <see cref="WinningProviderId"/> is a nullable TEXT Guid;
    /// <see cref="LastScoredAt"/> is TEXT ISO-8601.
    /// </summary>
    private sealed class CanonicalValueRow
    {
        public string  EntityId          { get; set; } = string.Empty;
        public string  Key               { get; set; } = string.Empty;
        public string  Value             { get; set; } = string.Empty;
        public string  LastScoredAt      { get; set; } = string.Empty;
        public int     IsConflicted      { get; set; }
        public string? WinningProviderId { get; set; }
        public int     NeedsReview       { get; set; }
    }

    private static CanonicalValue MapRow(CanonicalValueRow r)
    {
        Guid? winningProviderId = null;
        if (r.WinningProviderId is not null)
        {
            if (Guid.TryParse(r.WinningProviderId, out var parsed))
                winningProviderId = parsed;
        }

        return new CanonicalValue
        {
            EntityId          = Guid.Parse(r.EntityId),
            Key               = r.Key,
            Value             = r.Value,
            LastScoredAt      = DateTimeOffset.Parse(r.LastScoredAt),
            IsConflicted      = r.IsConflicted == 1,
            WinningProviderId = winningProviderId,
            NeedsReview       = r.NeedsReview == 1,
        };
    }
}
