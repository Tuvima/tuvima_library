using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="ICanonicalValueRepository"/>.
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
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                // INSERT OR REPLACE honours the (entity_id, key) PRIMARY KEY:
                // if a row already exists it is deleted then re-inserted with the
                // new value and timestamp.
                cmd.CommandText = """
                    INSERT OR REPLACE INTO canonical_values
                        (entity_id, key, value, last_scored_at, is_conflicted, winning_provider_id, needs_review)
                    VALUES
                        (@entity_id, @key, @value, @last_scored_at, @is_conflicted, @winning_provider_id, @needs_review);
                    """;

                var pEntityId           = cmd.Parameters.Add("@entity_id",           SqliteType.Text);
                var pKey                = cmd.Parameters.Add("@key",                 SqliteType.Text);
                var pValue              = cmd.Parameters.Add("@value",               SqliteType.Text);
                var pLastScoredAt       = cmd.Parameters.Add("@last_scored_at",      SqliteType.Text);
                var pIsConflicted       = cmd.Parameters.Add("@is_conflicted",       SqliteType.Integer);
                var pWinningProviderId  = cmd.Parameters.Add("@winning_provider_id", SqliteType.Text);
                var pNeedsReview        = cmd.Parameters.Add("@needs_review",        SqliteType.Integer);

                foreach (var cv in values)
                {
                    ct.ThrowIfCancellationRequested();

                    pEntityId.Value          = cv.EntityId.ToString();
                    pKey.Value               = cv.Key;
                    pValue.Value             = cv.Value;
                    pLastScoredAt.Value      = cv.LastScoredAt.ToString("o"); // ISO-8601 round-trip
                    pIsConflicted.Value      = cv.IsConflicted ? 1 : 0;
                    pWinningProviderId.Value = cv.WinningProviderId.HasValue
                        ? (object)cv.WinningProviderId.Value.ToString()
                        : DBNull.Value;
                    pNeedsReview.Value       = cv.NeedsReview ? 1 : 0;

                    cmd.ExecuteNonQuery();
                }

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, key, value, last_scored_at, is_conflicted, winning_provider_id, needs_review
            FROM   canonical_values
            WHERE  entity_id = @entity_id
            ORDER  BY key ASC;
            """;
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());

        var results = new List<CanonicalValue>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CanonicalValue>> GetConflictedAsync(
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, key, value, last_scored_at, is_conflicted, winning_provider_id, needs_review
            FROM   canonical_values
            WHERE  is_conflicted = 1
            ORDER  BY last_scored_at DESC;
            """;

        var results = new List<CanonicalValue>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(results);
    }

    /// <inheritdoc/>
    public async Task DeleteByEntityAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM canonical_values WHERE entity_id = @entity_id;";
            cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
            cmd.ExecuteNonQuery();
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
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM canonical_values WHERE entity_id = @entity_id AND key = @key;";
            cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
            cmd.Parameters.AddWithValue("@key", key);
            cmd.ExecuteNonQuery();
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id
            FROM   canonical_values
            WHERE  key   = @key   COLLATE NOCASE
              AND  value = @value COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("@key",   key);
        cmd.Parameters.AddWithValue("@value", value);

        var results = new List<Guid>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(Guid.Parse(reader.GetString(0)));
        }

        return Task.FromResult<IReadOnlyList<Guid>>(results);
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, key, value, last_scored_at, is_conflicted, winning_provider_id, needs_review
            FROM   canonical_values
            WHERE  key   = @key   COLLATE NOCASE
              AND  value LIKE @prefix || '%';
            """;
        cmd.Parameters.AddWithValue("@key",    key);
        cmd.Parameters.AddWithValue("@prefix", prefix);

        var results = new List<CanonicalValue>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<CanonicalValue>>(results);
    }

    // -------------------------------------------------------------------------
    // Private row mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the current reader row to a <see cref="CanonicalValue"/>.
    /// Column ordinals match the SELECT list used in every query in this class:
    ///   0=entity_id, 1=key, 2=value, 3=last_scored_at, 4=is_conflicted,
    ///   5=winning_provider_id (nullable TEXT), 6=needs_review
    /// </summary>
    private static CanonicalValue MapRow(SqliteDataReader r)
    {
        Guid? winningProviderId = null;
        if (!r.IsDBNull(5))
        {
            var raw = r.GetString(5);
            if (Guid.TryParse(raw, out var parsed))
                winningProviderId = parsed;
        }

        return new CanonicalValue
        {
            EntityId          = Guid.Parse(r.GetString(0)),
            Key               = r.GetString(1),
            Value             = r.GetString(2),
            LastScoredAt      = DateTimeOffset.Parse(r.GetString(3)),
            IsConflicted      = r.GetInt32(4) == 1,
            WinningProviderId = winningProviderId,
            NeedsReview       = !r.IsDBNull(6) && r.GetInt32(6) == 1,
        };
    }
}
