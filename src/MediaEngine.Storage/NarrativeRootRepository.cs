using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="INarrativeRootRepository"/>.
///
/// Narrative roots track the fictional universe hierarchy (Universe → Franchise → Series).
/// The QID is the primary key — upserts update label, level, and parent on conflict.
///
/// Thread safety: same serialised-connection model as <see cref="PersonRepository"/>.
/// </summary>
public sealed class NarrativeRootRepository : INarrativeRootRepository
{
    private readonly IDatabaseConnection _db;

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT qid, label, level, parent_qid, created_at
            FROM   narrative_roots
            WHERE  qid = @qid
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        using var reader = cmd.ExecuteReader();
        return Task.FromResult(reader.Read() ? MapRow(reader) : null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<NarrativeRoot>> ListAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT qid, label, level, parent_qid, created_at
            FROM   narrative_roots
            ORDER BY level, label;
            """;

        var result = new List<NarrativeRoot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<NarrativeRoot>>(result);
    }

    /// <inheritdoc/>
    public Task UpsertAsync(NarrativeRoot root, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(root);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO narrative_roots (qid, label, level, parent_qid, created_at)
            VALUES (@qid, @label, @level, @parentQid, @createdAt)
            ON CONFLICT(qid) DO UPDATE SET
                label = excluded.label,
                level = excluded.level,
                parent_qid = excluded.parent_qid;
            """;
        cmd.Parameters.AddWithValue("@qid", root.Qid);
        cmd.Parameters.AddWithValue("@label", root.Label);
        cmd.Parameters.AddWithValue("@level", root.Level);
        cmd.Parameters.AddWithValue("@parentQid", (object?)root.ParentQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", root.CreatedAt.ToString("o"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<NarrativeRoot>> GetChildrenAsync(
        string parentQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(parentQid);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT qid, label, level, parent_qid, created_at
            FROM   narrative_roots
            WHERE  parent_qid = @parentQid
            ORDER BY level, label;
            """;
        cmd.Parameters.AddWithValue("@parentQid", parentQid);

        var result = new List<NarrativeRoot>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<NarrativeRoot>>(result);
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM narrative_roots;";
        return Task.FromResult(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    // ── Row Mapper ──────────────────────────────────────────────────────────

    private static NarrativeRoot MapRow(SqliteDataReader r) => new()
    {
        Qid       = r.GetString(0),
        Label     = r.GetString(1),
        Level     = r.GetString(2),
        ParentQid = r.IsDBNull(3) ? null : r.GetString(3),
        CreatedAt = DateTimeOffset.Parse(r.GetString(4)),
    };
}
