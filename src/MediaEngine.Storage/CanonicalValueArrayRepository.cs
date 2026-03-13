using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="ICanonicalValueArrayRepository"/>.
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
                using (var delCmd = conn.CreateCommand())
                {
                    delCmd.Transaction = tx;
                    delCmd.CommandText = """
                        DELETE FROM canonical_value_arrays
                        WHERE entity_id = @entity_id AND key = @key;
                        """;
                    delCmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
                    delCmd.Parameters.AddWithValue("@key", key);
                    delCmd.ExecuteNonQuery();
                }

                if (entries.Count > 0)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO canonical_value_arrays
                            (entity_id, key, ordinal, value, value_qid)
                        VALUES
                            (@entity_id, @key, @ordinal, @value, @value_qid);
                        """;

                    var pEntityId = cmd.Parameters.Add("@entity_id", SqliteType.Text);
                    var pKey      = cmd.Parameters.Add("@key",       SqliteType.Text);
                    var pOrdinal  = cmd.Parameters.Add("@ordinal",   SqliteType.Integer);
                    var pValue    = cmd.Parameters.Add("@value",     SqliteType.Text);
                    var pValueQid = cmd.Parameters.Add("@value_qid", SqliteType.Text);

                    foreach (var entry in entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        pEntityId.Value = entityId.ToString();
                        pKey.Value      = key;
                        pOrdinal.Value  = entry.Ordinal;
                        pValue.Value    = entry.Value;
                        pValueQid.Value = (object?)entry.ValueQid ?? DBNull.Value;
                        cmd.ExecuteNonQuery();
                    }
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT ordinal, value, value_qid
            FROM canonical_value_arrays
            WHERE entity_id = @entity_id AND key = @key
            ORDER BY ordinal ASC;
            """;
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
        cmd.Parameters.AddWithValue("@key", key);

        var results = new List<CanonicalArrayEntry>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(MapRow(reader));
        }

        return Task.FromResult<IReadOnlyList<CanonicalArrayEntry>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, IReadOnlyList<CanonicalArrayEntry>>> GetAllByEntityAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT key, ordinal, value, value_qid
            FROM canonical_value_arrays
            WHERE entity_id = @entity_id
            ORDER BY key ASC, ordinal ASC;
            """;
        cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());

        var results = new Dictionary<string, List<CanonicalArrayEntry>>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            var key = reader.GetString(0);
            if (!results.TryGetValue(key, out var list))
            {
                list = [];
                results[key] = list;
            }
            list.Add(new CanonicalArrayEntry
            {
                Ordinal  = reader.GetInt32(1),
                Value    = reader.GetString(2),
                ValueQid = reader.IsDBNull(3) ? null : reader.GetString(3),
            });
        }

        // Cast to read-only interfaces.
        var readOnly = new Dictionary<string, IReadOnlyList<CanonicalArrayEntry>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (key, list) in results)
            readOnly[key] = list;

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
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM canonical_value_arrays WHERE entity_id = @entity_id;";
            cmd.Parameters.AddWithValue("@entity_id", entityId.ToString());
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    // -------------------------------------------------------------------------
    // Private
    // -------------------------------------------------------------------------

    private static CanonicalArrayEntry MapRow(SqliteDataReader r) => new()
    {
        Ordinal  = r.GetInt32(0),
        Value    = r.GetString(1),
        ValueQid = r.IsDBNull(2) ? null : r.GetString(2),
    };
}
