using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="INarrativeRootRepository"/>.
/// Uses Dapper for type-safe column-to-property mapping.
///
/// Narrative roots track the fictional universe hierarchy (Universe → Franchise → Series).
/// The QID is the primary key — upserts update label, level, and parent on conflict.
/// </summary>
public sealed class NarrativeRootRepository : INarrativeRootRepository
{
    private readonly IDatabaseConnection _db;

    private const string SelectColumns = """
        qid        AS Qid,
        label      AS Label,
        level      AS Level,
        parent_qid AS ParentQid,
        created_at AS CreatedAt
        """;

    public NarrativeRootRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<NarrativeRoot?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<NarrativeRoot>($"""
            SELECT {SelectColumns}
            FROM   narrative_roots
            WHERE  qid = @qid
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<NarrativeRoot>> ListAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.Query<NarrativeRoot>($"""
            SELECT {SelectColumns}
            FROM   narrative_roots
            ORDER BY level, label;
            """).AsList();

        return Task.FromResult<IReadOnlyList<NarrativeRoot>>(result);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(NarrativeRoot root, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(root);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO narrative_roots (qid, label, level, parent_qid, created_at)
            VALUES (@qid, @label, @level, @parentQid, @createdAt)
            ON CONFLICT(qid) DO UPDATE SET
                label = excluded.label,
                level = excluded.level,
                parent_qid = excluded.parent_qid;
            """,
            new
            {
                qid       = root.Qid,
                label     = root.Label,
                level     = root.Level,
                parentQid = root.ParentQid,
                createdAt = root.CreatedAt.ToString("o"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<NarrativeRoot>> GetChildrenAsync(
        string parentQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(parentQid);

        using var conn = _db.CreateConnection();
        var result = conn.Query<NarrativeRoot>($"""
            SELECT {SelectColumns}
            FROM   narrative_roots
            WHERE  parent_qid = @parentQid
            ORDER BY level, label;
            """, new { parentQid }).AsList();

        return Task.FromResult<IReadOnlyList<NarrativeRoot>>(result);
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM narrative_roots;");
        return Task.FromResult(count);
    }
}
