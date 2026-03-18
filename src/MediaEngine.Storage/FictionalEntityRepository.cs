using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IFictionalEntityRepository"/>.
///
/// Fictional entities (Characters, Locations, Organizations) are discovered
/// during work hydration and enriched asynchronously via Wikidata SPARQL.
/// Work-link junction records live in <c>fictional_entity_work_links</c>.
/// </summary>
public sealed class FictionalEntityRepository : IFictionalEntityRepository
{
    private readonly IDatabaseConnection _db;

    public FictionalEntityRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    /// <inheritdoc/>
    public Task<FictionalEntity?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(qid);

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<FictionalEntity>("""
            SELECT id                      AS Id,
                   wikidata_qid            AS WikidataQid,
                   label                   AS Label,
                   description             AS Description,
                   entity_sub_type         AS EntitySubType,
                   fictional_universe_qid  AS FictionalUniverseQid,
                   fictional_universe_label AS FictionalUniverseLabel,
                   image_url               AS ImageUrl,
                   local_image_path        AS LocalImagePath,
                   created_at              AS CreatedAt,
                   enriched_at             AS EnrichedAt
            FROM   fictional_entities
            WHERE  wikidata_qid = @qid COLLATE NOCASE
            LIMIT  1;
            """, new { qid });
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<FictionalEntity?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.QueryFirstOrDefault<FictionalEntity>("""
            SELECT id                      AS Id,
                   wikidata_qid            AS WikidataQid,
                   label                   AS Label,
                   description             AS Description,
                   entity_sub_type         AS EntitySubType,
                   fictional_universe_qid  AS FictionalUniverseQid,
                   fictional_universe_label AS FictionalUniverseLabel,
                   image_url               AS ImageUrl,
                   local_image_path        AS LocalImagePath,
                   created_at              AS CreatedAt,
                   enriched_at             AS EnrichedAt
            FROM   fictional_entities
            WHERE  id = @id
            LIMIT  1;
            """, new { id });
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAsync(
        string universeQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(universeQid);

        using var conn = _db.CreateConnection();
        var results = conn.Query<FictionalEntity>("""
            SELECT id                      AS Id,
                   wikidata_qid            AS WikidataQid,
                   label                   AS Label,
                   description             AS Description,
                   entity_sub_type         AS EntitySubType,
                   fictional_universe_qid  AS FictionalUniverseQid,
                   fictional_universe_label AS FictionalUniverseLabel,
                   image_url               AS ImageUrl,
                   local_image_path        AS LocalImagePath,
                   created_at              AS CreatedAt,
                   enriched_at             AS EnrichedAt
            FROM   fictional_entities
            WHERE  fictional_universe_qid = @universeQid
            ORDER BY entity_sub_type, label;
            """, new { universeQid }).AsList();

        return Task.FromResult<IReadOnlyList<FictionalEntity>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAndTypeAsync(
        string universeQid, string entitySubType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<FictionalEntity>("""
            SELECT id                      AS Id,
                   wikidata_qid            AS WikidataQid,
                   label                   AS Label,
                   description             AS Description,
                   entity_sub_type         AS EntitySubType,
                   fictional_universe_qid  AS FictionalUniverseQid,
                   fictional_universe_label AS FictionalUniverseLabel,
                   image_url               AS ImageUrl,
                   local_image_path        AS LocalImagePath,
                   created_at              AS CreatedAt,
                   enriched_at             AS EnrichedAt
            FROM   fictional_entities
            WHERE  fictional_universe_qid = @universeQid
              AND  entity_sub_type = @entitySubType
            ORDER BY label;
            """, new { universeQid, entitySubType }).AsList();

        return Task.FromResult<IReadOnlyList<FictionalEntity>>(results);
    }

    /// <inheritdoc/>
    public Task CreateAsync(FictionalEntity entity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT INTO fictional_entities
                (id, wikidata_qid, label, description, entity_sub_type,
                 fictional_universe_qid, fictional_universe_label,
                 image_url, local_image_path, created_at, enriched_at)
            VALUES
                (@Id, @WikidataQid, @Label, @Description, @EntitySubType,
                 @FictionalUniverseQid, @FictionalUniverseLabel,
                 @ImageUrl, @LocalImagePath, @CreatedAt, @EnrichedAt);
            """,
            new
            {
                Id                    = entity.Id,
                entity.WikidataQid,
                entity.Label,
                entity.Description,
                entity.EntitySubType,
                entity.FictionalUniverseQid,
                entity.FictionalUniverseLabel,
                entity.ImageUrl,
                entity.LocalImagePath,
                CreatedAt             = entity.CreatedAt,
                EnrichedAt            = entity.EnrichedAt,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UpdateEnrichmentAsync(
        Guid entityId,
        string? description,
        string? imageUrl,
        DateTimeOffset enrichedAt,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE fictional_entities
            SET    description = @description,
                   image_url   = @imageUrl,
                   enriched_at = @enrichedAt
            WHERE  id = @entityId;
            """,
            new
            {
                entityId,
                description,
                imageUrl,
                enrichedAt,
            });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task LinkToWorkAsync(
        Guid entityId, string workQid, string? workLabel,
        string linkType = "appears_in", CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT OR IGNORE INTO fictional_entity_work_links
                (entity_id, work_qid, work_label, link_type)
            VALUES
                (@entityId, @workQid, @workLabel, @linkType);
            """,
            new { entityId, workQid, workLabel, linkType });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>
        GetWorkLinksAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var rows = conn.Query<WorkLinkRow>("""
            SELECT work_qid   AS WorkQid,
                   work_label AS WorkLabel,
                   link_type  AS LinkType
            FROM   fictional_entity_work_links
            WHERE  entity_id = @entityId
            ORDER BY work_qid;
            """, new { entityId }).AsList();

        var result = rows
            .ConvertAll(r => (r.WorkQid, r.WorkLabel, r.LinkType));

        return Task.FromResult<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>(result);
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM fictional_entities;");
        return Task.FromResult(count);
    }

    /// <inheritdoc/>
    public Task UpdateRevisionAsync(Guid entityId, long revisionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE fictional_entities
            SET    wikidata_revision_id = @revisionId
            WHERE  id = @entityId;
            """,
            new { entityId, revisionId });
        return Task.CompletedTask;
    }

    // ── Private row types ────────────────────────────────────────────────────

    /// <summary>Intermediate row type for <see cref="GetWorkLinksAsync"/>.</summary>
    private sealed class WorkLinkRow
    {
        public string  WorkQid   { get; set; } = string.Empty;
        public string? WorkLabel { get; set; }
        public string  LinkType  { get; set; } = string.Empty;
    }
}
