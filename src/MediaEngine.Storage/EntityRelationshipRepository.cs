using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IEntityRelationshipRepository"/>.
///
/// Graph edges are inserted idempotently (duplicate subject+type+object silently ignored).
/// Lookups support both directions (outgoing from subject, incoming to object) for
/// bidirectional graph traversal.
///
/// Thread safety: same serialised-connection model as <see cref="PersonRepository"/>.
/// </summary>
public sealed class EntityRelationshipRepository : IEntityRelationshipRepository
{
    private readonly IDatabaseConnection _db;

    public EntityRelationshipRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task CreateAsync(EntityRelationship edge, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(edge);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO entity_relationships
                (id, subject_qid, relationship_type, object_qid,
                 confidence, context_work_qid, discovered_at)
            VALUES
                (@id, @subjectQid, @relType, @objectQid,
                 @confidence, @contextWorkQid, @discoveredAt);
            """;
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("@subjectQid", edge.SubjectQid);
        cmd.Parameters.AddWithValue("@relType", edge.RelationshipTypeValue);
        cmd.Parameters.AddWithValue("@objectQid", edge.ObjectQid);
        cmd.Parameters.AddWithValue("@confidence", edge.Confidence);
        cmd.Parameters.AddWithValue("@contextWorkQid", (object?)edge.ContextWorkQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@discoveredAt", edge.DiscoveredAt.ToString("o"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityRelationship>> GetBySubjectAsync(
        string subjectQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(QueryEdges("subject_qid = @qid", subjectQid));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityRelationship>> GetByObjectAsync(
        string objectQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(QueryEdges("object_qid = @qid", objectQid));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityRelationship>> GetByEntityAsync(
        string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(QueryEdges(
            "subject_qid = @qid OR object_qid = @qid", qid));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<EntityRelationship>> GetByUniverseAsync(
        IReadOnlyCollection<string> entityQids, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (entityQids.Count == 0)
            return Task.FromResult<IReadOnlyList<EntityRelationship>>([]);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        // Build IN clause with parameterized values
        var paramNames = new List<string>(entityQids.Count);
        var idx = 0;
        foreach (var qid in entityQids)
        {
            var paramName = $"@q{idx}";
            paramNames.Add(paramName);
            cmd.Parameters.AddWithValue(paramName, qid);
            idx++;
        }

        var inClause = string.Join(", ", paramNames);
        cmd.CommandText = $"""
            SELECT subject_qid, relationship_type, object_qid,
                   confidence, context_work_qid, discovered_at
            FROM   entity_relationships
            WHERE  subject_qid IN ({inClause})
              AND  object_qid  IN ({inClause})
            ORDER BY subject_qid, relationship_type;
            """;

        var result = new List<EntityRelationship>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<EntityRelationship>>(result);
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM entity_relationships;";
        return Task.FromResult(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private IReadOnlyList<EntityRelationship> QueryEdges(string whereClause, string qid)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT subject_qid, relationship_type, object_qid,
                   confidence, context_work_qid, discovered_at
            FROM   entity_relationships
            WHERE  {whereClause}
            ORDER BY relationship_type, object_qid;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        var result = new List<EntityRelationship>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return result;
    }

    private static EntityRelationship MapRow(SqliteDataReader r) => new()
    {
        SubjectQid            = r.GetString(0),
        RelationshipTypeValue = r.GetString(1),
        ObjectQid             = r.GetString(2),
        Confidence            = r.GetDouble(3),
        ContextWorkQid        = r.IsDBNull(4) ? null : r.GetString(4),
        DiscoveredAt          = DateTimeOffset.Parse(r.GetString(5)),
    };
}
