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
            var conn = _db.Open();

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
                        (entity_id, key, value, last_scored_at, is_conflicted)
                    VALUES
                        (@entity_id, @key, @value, @last_scored_at, @is_conflicted);
                    """;

                var pEntityId      = cmd.Parameters.Add("@entity_id",      SqliteType.Text);
                var pKey           = cmd.Parameters.Add("@key",            SqliteType.Text);
                var pValue         = cmd.Parameters.Add("@value",          SqliteType.Text);
                var pLastScoredAt  = cmd.Parameters.Add("@last_scored_at", SqliteType.Text);
                var pIsConflicted  = cmd.Parameters.Add("@is_conflicted",  SqliteType.Integer);

                foreach (var cv in values)
                {
                    ct.ThrowIfCancellationRequested();

                    pEntityId.Value     = cv.EntityId.ToString();
                    pKey.Value          = cv.Key;
                    pValue.Value        = cv.Value;
                    pLastScoredAt.Value = cv.LastScoredAt.ToString("o"); // ISO-8601 round-trip
                    pIsConflicted.Value = cv.IsConflicted ? 1 : 0;

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

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, key, value, last_scored_at, is_conflicted
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

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT entity_id, key, value, last_scored_at, is_conflicted
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
            var conn = _db.Open();
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

    // -------------------------------------------------------------------------
    // Private row mapper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps the current reader row to a <see cref="CanonicalValue"/>.
    /// Column ordinals match the SELECT list used in every query in this class:
    ///   0=entity_id, 1=key, 2=value, 3=last_scored_at, 4=is_conflicted
    /// </summary>
    private static CanonicalValue MapRow(SqliteDataReader r) => new()
    {
        EntityId      = Guid.Parse(r.GetString(0)),
        Key           = r.GetString(1),
        Value         = r.GetString(2),
        LastScoredAt  = DateTimeOffset.Parse(r.GetString(3)),
        IsConflicted  = r.GetInt32(4) == 1,
    };
}
