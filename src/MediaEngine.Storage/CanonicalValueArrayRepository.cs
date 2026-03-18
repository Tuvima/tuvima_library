using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="ICanonicalValueArrayRepository"/>.
///
/// Multi-valued canonical fields (genre, characters, cast_member, etc.) are stored
/// as individual rows in <c>canonical_value_arrays</c> rather than as
/// <c>|||</c>-separated strings. Each row carries an ordinal for display ordering
/// and an optional QID for entity-valued items.
/// </summary>
public sealed class CanonicalValueArrayRepository : ICanonicalValueArrayRepository
{
    private readonly IDatabaseConnection _db;

    public CanonicalValueArrayRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public async Task SetValuesAsync(
        Guid entityId,
        string key,
        IReadOnlyList<CanonicalArrayEntry> entries,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                // Delete existing entries for this (entity, key) pair.
                conn.Execute("""
                    DELETE FROM canonical_value_arrays
                    WHERE entity_id = @entityId AND key = @key;
                    """,
                    new { entityId, key },
                    transaction: tx);

                if (entries.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    conn.Execute("""
                        INSERT INTO canonical_value_arrays
                            (entity_id, key, ordinal, value, value_qid)
                        VALUES
                            (@EntityId, @Key, @Ordinal, @Value, @ValueQid);
                        """,
                        entries.Select(e => new
                        {
                            EntityId = entityId.ToString(),
                            Key      = key,
                            e.Ordinal,
                            e.Value,
                            ValueQid = e.ValueQid,
                        }),
                        transaction: tx);
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
    public Task<IReadOnlyList<CanonicalArrayEntry>> GetValuesAsync(
        Guid entityId,
        string key,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        // Use intermediate row type because CanonicalArrayEntry has init-only properties
        // which Dapper cannot set directly.
        var rows = conn.Query<CanonicalArrayKeyedRow>("""
            SELECT ordinal   AS Ordinal,
                   value     AS Value,
                   value_qid AS ValueQid
            FROM canonical_value_arrays
            WHERE entity_id = @entityId AND key = @key
            ORDER BY ordinal ASC;
            """, new { entityId, key }).AsList();

        var results = rows.ConvertAll(r => new CanonicalArrayEntry
        {
            Ordinal  = r.Ordinal,
            Value    = r.Value,
            ValueQid = r.ValueQid,
        });

        return Task.FromResult<IReadOnlyList<CanonicalArrayEntry>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<CanonicalArrayEntry>>> GetAllByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<CanonicalArrayKeyedRow>("""
            SELECT key       AS Key,
                   ordinal   AS Ordinal,
                   value     AS Value,
                   value_qid AS ValueQid
            FROM canonical_value_arrays
            WHERE entity_id = @entityId
            ORDER BY key ASC, ordinal ASC;
            """, new { entityId }).AsList();

        var grouped = new Dictionary<string, List<CanonicalArrayEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (!grouped.TryGetValue(row.Key, out var list))
            {
                list = [];
                grouped[row.Key] = list;
            }
            list.Add(new CanonicalArrayEntry
            {
                Ordinal  = row.Ordinal,
                Value    = row.Value,
                ValueQid = row.ValueQid,
            });
        }

        var readOnly = new Dictionary<string, IReadOnlyList<CanonicalArrayEntry>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (k, list) in grouped)
            readOnly[k] = list;

        return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<CanonicalArrayEntry>>>(readOnly);
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
                "DELETE FROM canonical_value_arrays WHERE entity_id = @entityId;",
                new { entityId });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    // ── Private intermediate row type ─────────────────────────────────────────

    /// <summary>
    /// Intermediate row type for <see cref="GetAllByEntityAsync"/> that includes
    /// the grouping key alongside the entry fields.
    /// </summary>
    private sealed class CanonicalArrayKeyedRow
    {
        public string  Key      { get; set; } = string.Empty;
        public int     Ordinal  { get; set; }
        public string  Value    { get; set; } = string.Empty;
        public string? ValueQid { get; set; }
    }
}
