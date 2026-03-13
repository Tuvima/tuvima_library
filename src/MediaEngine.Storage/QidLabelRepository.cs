using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IQidLabelRepository"/>.
///
/// The <c>qid_labels</c> table caches Wikidata QID-to-label mappings
/// locally so that display labels can be resolved without network access.
/// </summary>
public sealed class QidLabelRepository : IQidLabelRepository
{
    private readonly IDatabaseConnection _db;

    public QidLabelRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<string?> GetLabelAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT label FROM qid_labels WHERE qid = @qid;";
        cmd.Parameters.AddWithValue("@qid", qid);

        var result = cmd.ExecuteScalar() as string;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyDictionary<string, string>> GetLabelsAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var qidList = qids.ToList();
        if (qidList.Count == 0)
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());

        using var conn = _db.CreateConnection();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // SQLite doesn't support array parameters, so we batch in chunks.
        foreach (var chunk in Chunk(qidList, 500))
        {
            ct.ThrowIfCancellationRequested();

            using var cmd = conn.CreateCommand();
            var placeholders = new string[chunk.Count];
            for (int i = 0; i < chunk.Count; i++)
            {
                placeholders[i] = $"@q{i}";
                cmd.Parameters.AddWithValue($"@q{i}", chunk[i]);
            }

            cmd.CommandText = $"""
                SELECT qid, label FROM qid_labels
                WHERE qid IN ({string.Join(',', placeholders)});
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result[reader.GetString(0)] = reader.GetString(1);
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        string qid,
        string label,
        string? description,
        string? entityType,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO qid_labels (qid, label, description, entity_type, fetched_at, updated_at)
                VALUES (@qid, @label, @description, @entity_type, @now, @now)
                ON CONFLICT(qid) DO UPDATE SET
                    label       = excluded.label,
                    description = excluded.description,
                    entity_type = COALESCE(excluded.entity_type, qid_labels.entity_type),
                    updated_at  = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("@qid", qid);
            cmd.Parameters.AddWithValue("@label", label);
            cmd.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@entity_type", (object?)entityType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public async Task UpsertBatchAsync(
        IReadOnlyList<QidLabel> labels,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (labels.Count == 0) return;

        await _db.AcquireWriteLockAsync(ct).ConfigureAwait(false);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO qid_labels (qid, label, description, entity_type, fetched_at, updated_at)
                    VALUES (@qid, @label, @description, @entity_type, @fetched_at, @updated_at)
                    ON CONFLICT(qid) DO UPDATE SET
                        label       = excluded.label,
                        description = excluded.description,
                        entity_type = COALESCE(excluded.entity_type, qid_labels.entity_type),
                        updated_at  = excluded.updated_at;
                    """;

                var pQid         = cmd.Parameters.Add("@qid",         SqliteType.Text);
                var pLabel       = cmd.Parameters.Add("@label",       SqliteType.Text);
                var pDescription = cmd.Parameters.Add("@description", SqliteType.Text);
                var pEntityType  = cmd.Parameters.Add("@entity_type", SqliteType.Text);
                var pFetchedAt   = cmd.Parameters.Add("@fetched_at",  SqliteType.Text);
                var pUpdatedAt   = cmd.Parameters.Add("@updated_at",  SqliteType.Text);

                foreach (var entry in labels)
                {
                    ct.ThrowIfCancellationRequested();
                    pQid.Value         = entry.Qid;
                    pLabel.Value       = entry.Label;
                    pDescription.Value = (object?)entry.Description ?? DBNull.Value;
                    pEntityType.Value  = (object?)entry.EntityType ?? DBNull.Value;
                    pFetchedAt.Value   = entry.FetchedAt.ToString("o");
                    pUpdatedAt.Value   = entry.UpdatedAt.ToString("o");
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
    public Task<IReadOnlyList<QidLabel>> GetLabelDetailsAsync(
        IEnumerable<string> qids,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var qidList = qids.ToList();
        if (qidList.Count == 0)
            return Task.FromResult<IReadOnlyList<QidLabel>>([]);

        using var conn = _db.CreateConnection();
        var results = new List<QidLabel>();

        foreach (var chunk in Chunk(qidList, 500))
        {
            ct.ThrowIfCancellationRequested();

            using var cmd = conn.CreateCommand();
            var placeholders = new string[chunk.Count];
            for (int i = 0; i < chunk.Count; i++)
            {
                placeholders[i] = $"@q{i}";
                cmd.Parameters.AddWithValue($"@q{i}", chunk[i]);
            }

            cmd.CommandText = $"""
                SELECT qid, label, description, entity_type, fetched_at, updated_at
                FROM qid_labels
                WHERE qid IN ({string.Join(',', placeholders)});
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new QidLabel
                {
                    Qid         = reader.GetString(0),
                    Label       = reader.GetString(1),
                    Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                    EntityType  = reader.IsDBNull(3) ? null : reader.GetString(3),
                    FetchedAt   = DateTimeOffset.Parse(reader.GetString(4)),
                    UpdatedAt   = DateTimeOffset.Parse(reader.GetString(5)),
                });
            }
        }

        return Task.FromResult<IReadOnlyList<QidLabel>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<QidLabel>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT qid, label, description, entity_type, fetched_at, updated_at
            FROM qid_labels ORDER BY qid;
            """;

        var results = new List<QidLabel>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            results.Add(new QidLabel
            {
                Qid         = reader.GetString(0),
                Label       = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                EntityType  = reader.IsDBNull(3) ? null : reader.GetString(3),
                FetchedAt   = DateTimeOffset.Parse(reader.GetString(4)),
                UpdatedAt   = DateTimeOffset.Parse(reader.GetString(5)),
            });
        }

        return Task.FromResult<IReadOnlyList<QidLabel>>(results);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += size)
            chunks.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return chunks;
    }
}
