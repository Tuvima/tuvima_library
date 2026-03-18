using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IEntityRelationshipRepository"/>.
///
/// Graph edges are inserted idempotently (duplicate subject+type+object silently ignored).
/// Lookups support both directions (outgoing from subject, incoming to object) for
/// bidirectional graph traversal.
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
        conn.Execute("""
            INSERT OR IGNORE INTO entity_relationships
                (id, subject_qid, relationship_type, object_qid,
                 confidence, context_work_qid, discovered_at,
                 start_time, end_time)
            VALUES
                (@Id, @SubjectQid, @RelType, @ObjectQid,
                 @Confidence, @ContextWorkQid, @DiscoveredAt,
                 @StartTime, @EndTime);
            """,
            new
            {
                Id             = Guid.NewGuid(),
                SubjectQid     = edge.SubjectQid,
                RelType        = edge.RelationshipTypeValue,
                ObjectQid      = edge.ObjectQid,
                edge.Confidence,
                edge.ContextWorkQid,
                DiscoveredAt   = edge.DiscoveredAt,
                edge.StartTime,
                edge.EndTime,
            });
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

        // Dapper expands @qids into a parameterised IN clause automatically.
        using var conn = _db.CreateConnection();
        var rows = conn.Query<EntityRelationshipRow>("""
            SELECT subject_qid       AS SubjectQid,
                   relationship_type AS RelationshipTypeValue,
                   object_qid        AS ObjectQid,
                   confidence        AS Confidence,
                   context_work_qid  AS ContextWorkQid,
                   discovered_at     AS DiscoveredAt,
                   start_time        AS StartTime,
                   end_time          AS EndTime
            FROM   entity_relationships
            WHERE  subject_qid IN @qids
              AND  object_qid  IN @qids
            ORDER BY subject_qid, relationship_type;
            """, new { qids = entityQids }).AsList();

        return Task.FromResult<IReadOnlyList<EntityRelationship>>(rows.ConvertAll(MapRow));
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM entity_relationships;");
        return Task.FromResult(count);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private IReadOnlyList<EntityRelationship> QueryEdges(string whereClause, string qid)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<EntityRelationshipRow>($"""
            SELECT subject_qid       AS SubjectQid,
                   relationship_type AS RelationshipTypeValue,
                   object_qid        AS ObjectQid,
                   confidence        AS Confidence,
                   context_work_qid  AS ContextWorkQid,
                   discovered_at     AS DiscoveredAt,
                   start_time        AS StartTime,
                   end_time          AS EndTime
            FROM   entity_relationships
            WHERE  {whereClause}
            ORDER BY relationship_type, object_qid;
            """, new { qid }).AsList();

        return rows.ConvertAll(MapRow);
    }

    // ── Private intermediate row type and mapper ──────────────────────────────

    /// <summary>
    /// Intermediate row type for Dapper mapping.
    /// <see cref="DiscoveredAt"/> uses the registered <c>DateTimeOffsetTypeHandler</c>.
    /// </summary>
    private sealed class EntityRelationshipRow
    {
        public string         SubjectQid            { get; set; } = string.Empty;
        public string         RelationshipTypeValue { get; set; } = string.Empty;
        public string         ObjectQid             { get; set; } = string.Empty;
        public double         Confidence            { get; set; }
        public string?        ContextWorkQid        { get; set; }
        public DateTimeOffset DiscoveredAt          { get; set; }
        public string?        StartTime             { get; set; }
        public string?        EndTime               { get; set; }
    }

    private static EntityRelationship MapRow(EntityRelationshipRow r) => new()
    {
        SubjectQid            = r.SubjectQid,
        RelationshipTypeValue = r.RelationshipTypeValue,
        ObjectQid             = r.ObjectQid,
        Confidence            = r.Confidence,
        ContextWorkQid        = r.ContextWorkQid,
        DiscoveredAt          = r.DiscoveredAt,
        StartTime             = r.StartTime,
        EndTime               = r.EndTime,
    };
}
