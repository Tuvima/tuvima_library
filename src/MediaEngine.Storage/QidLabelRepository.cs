using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IQidLabelRepository"/>.
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
        var result = conn.ExecuteScalar<string?>(
            "SELECT label FROM qid_labels WHERE qid = @qid;",
            new { qid });

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

            // Build numbered placeholders and a matching anonymous-object dictionary.
            var placeholders = new string[chunk.Count];
            var parameters   = new DynamicParameters();
            for (int i = 0; i < chunk.Count; i++)
            {
                placeholders[i] = $"@q{i}";
                parameters.Add($"q{i}", chunk[i]);
            }

            var sql = $"""
                SELECT qid AS Qid, label AS Label
                FROM qid_labels
                WHERE qid IN ({string.Join(',', placeholders)});
                """;

            foreach (var row in conn.Query<(string Qid, string Label)>(sql, parameters))
                result[row.Qid] = row.Label;
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
            conn.Execute("""
                INSERT INTO qid_labels (qid, label, description, entity_type, fetched_at, updated_at)
                VALUES (@qid, @label, @description, @entityType, @now, @now)
                ON CONFLICT(qid) DO UPDATE SET
                    label       = excluded.label,
                    description = excluded.description,
                    entity_type = COALESCE(excluded.entity_type, qid_labels.entity_type),
                    updated_at  = excluded.updated_at;
                """, new
            {
                qid,
                label,
                description,
                entityType,
                now = DateTimeOffset.UtcNow.ToString("o"),
            });
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
            using var tx   = conn.BeginTransaction();
            try
            {
                const string sql = """
                    INSERT INTO qid_labels (qid, label, description, entity_type, fetched_at, updated_at)
                    VALUES (@qid, @label, @description, @entityType, @fetchedAt, @updatedAt)
                    ON CONFLICT(qid) DO UPDATE SET
                        label       = excluded.label,
                        description = excluded.description,
                        entity_type = COALESCE(excluded.entity_type, qid_labels.entity_type),
                        updated_at  = excluded.updated_at;
                    """;

                foreach (var entry in labels)
                {
                    ct.ThrowIfCancellationRequested();
                    conn.Execute(sql, new
                    {
                        qid         = entry.Qid,
                        label       = entry.Label,
                        description = entry.Description,
                        entityType  = entry.EntityType,
                        fetchedAt   = entry.FetchedAt.ToString("o"),
                        updatedAt   = entry.UpdatedAt.ToString("o"),
                    }, tx);
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

            var placeholders = new string[chunk.Count];
            var parameters   = new DynamicParameters();
            for (int i = 0; i < chunk.Count; i++)
            {
                placeholders[i] = $"@q{i}";
                parameters.Add($"q{i}", chunk[i]);
            }

            var sql = $"""
                SELECT qid         AS Qid,
                       label       AS Label,
                       description AS Description,
                       entity_type AS EntityType,
                       fetched_at  AS FetchedAt,
                       updated_at  AS UpdatedAt
                FROM qid_labels
                WHERE qid IN ({string.Join(',', placeholders)});
                """;

            foreach (var row in conn.Query<QidLabelRow>(sql, parameters))
            {
                results.Add(new QidLabel
                {
                    Qid         = row.Qid,
                    Label       = row.Label,
                    Description = row.Description,
                    EntityType  = row.EntityType,
                    FetchedAt   = DateTimeOffset.Parse(row.FetchedAt),
                    UpdatedAt   = DateTimeOffset.Parse(row.UpdatedAt),
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
        var rows = conn.Query<QidLabelRow>("""
            SELECT qid         AS Qid,
                   label       AS Label,
                   description AS Description,
                   entity_type AS EntityType,
                   fetched_at  AS FetchedAt,
                   updated_at  AS UpdatedAt
            FROM qid_labels ORDER BY qid;
            """).AsList();

        ct.ThrowIfCancellationRequested();

        var results = rows.ConvertAll(row => new QidLabel
        {
            Qid         = row.Qid,
            Label       = row.Label,
            Description = row.Description,
            EntityType  = row.EntityType,
            FetchedAt   = DateTimeOffset.Parse(row.FetchedAt),
            UpdatedAt   = DateTimeOffset.Parse(row.UpdatedAt),
        });

        return Task.FromResult<IReadOnlyList<QidLabel>>(results);
    }

    // ── Private DTO ─────────────────────────────────────────────────────────
    // QidLabel uses required init-only properties; Dapper cannot construct it
    // directly. We read into a mutable DTO and convert in code.

    private sealed class QidLabelRow
    {
        public string  Qid         { get; set; } = string.Empty;
        public string  Label       { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? EntityType  { get; set; }
        public string  FetchedAt   { get; set; } = string.Empty;
        public string  UpdatedAt   { get; set; } = string.Empty;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static List<List<T>> Chunk<T>(List<T> source, int size)
    {
        var chunks = new List<List<T>>();
        for (int i = 0; i < source.Count; i += size)
            chunks.Add(source.GetRange(i, Math.Min(size, source.Count - i)));
        return chunks;
    }
}
