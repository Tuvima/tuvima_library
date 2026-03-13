using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IFictionalEntityRepository"/>.
///
/// Fictional entities (Characters, Locations, Organizations) are discovered
/// during work hydration and enriched asynchronously via Wikidata SPARQL.
/// Work-link junction records live in <c>fictional_entity_work_links</c>.
///
/// Thread safety: same serialised-connection model as <see cref="PersonRepository"/>.
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, wikidata_qid, label, description, entity_sub_type,
                   fictional_universe_qid, fictional_universe_label,
                   image_url, local_image_path, created_at, enriched_at
            FROM   fictional_entities
            WHERE  wikidata_qid = @qid COLLATE NOCASE
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        using var reader = cmd.ExecuteReader();
        return Task.FromResult(reader.Read() ? MapRow(reader) : null);
    }

    /// <inheritdoc/>
    public Task<FictionalEntity?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, wikidata_qid, label, description, entity_sub_type,
                   fictional_universe_qid, fictional_universe_label,
                   image_url, local_image_path, created_at, enriched_at
            FROM   fictional_entities
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());

        using var reader = cmd.ExecuteReader();
        return Task.FromResult(reader.Read() ? MapRow(reader) : null);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAsync(
        string universeQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(universeQid);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, wikidata_qid, label, description, entity_sub_type,
                   fictional_universe_qid, fictional_universe_label,
                   image_url, local_image_path, created_at, enriched_at
            FROM   fictional_entities
            WHERE  fictional_universe_qid = @universeQid
            ORDER BY entity_sub_type, label;
            """;
        cmd.Parameters.AddWithValue("@universeQid", universeQid);

        var result = new List<FictionalEntity>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<FictionalEntity>>(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<FictionalEntity>> GetByUniverseAndTypeAsync(
        string universeQid, string entitySubType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, wikidata_qid, label, description, entity_sub_type,
                   fictional_universe_qid, fictional_universe_label,
                   image_url, local_image_path, created_at, enriched_at
            FROM   fictional_entities
            WHERE  fictional_universe_qid = @universeQid
              AND  entity_sub_type = @subType
            ORDER BY label;
            """;
        cmd.Parameters.AddWithValue("@universeQid", universeQid);
        cmd.Parameters.AddWithValue("@subType", entitySubType);

        var result = new List<FictionalEntity>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result.Add(MapRow(reader));

        return Task.FromResult<IReadOnlyList<FictionalEntity>>(result);
    }

    /// <inheritdoc/>
    public Task CreateAsync(FictionalEntity entity, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entity);

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO fictional_entities
                (id, wikidata_qid, label, description, entity_sub_type,
                 fictional_universe_qid, fictional_universe_label,
                 image_url, local_image_path, created_at, enriched_at)
            VALUES
                (@id, @qid, @label, @description, @subType,
                 @universeQid, @universeLabel,
                 @imageUrl, @localImagePath, @createdAt, @enrichedAt);
            """;
        cmd.Parameters.AddWithValue("@id", entity.Id.ToString());
        cmd.Parameters.AddWithValue("@qid", entity.WikidataQid);
        cmd.Parameters.AddWithValue("@label", entity.Label);
        cmd.Parameters.AddWithValue("@description", (object?)entity.Description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@subType", entity.EntitySubType);
        cmd.Parameters.AddWithValue("@universeQid", (object?)entity.FictionalUniverseQid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@universeLabel", (object?)entity.FictionalUniverseLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@imageUrl", (object?)entity.ImageUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@localImagePath", (object?)entity.LocalImagePath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@createdAt", entity.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@enrichedAt", entity.EnrichedAt.HasValue
            ? entity.EnrichedAt.Value.ToString("o") : DBNull.Value);

        cmd.ExecuteNonQuery();
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE fictional_entities
            SET    description = @description,
                   image_url = @imageUrl,
                   enriched_at = @enrichedAt
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id", entityId.ToString());
        cmd.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@imageUrl", (object?)imageUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@enrichedAt", enrichedAt.ToString("o"));

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task LinkToWorkAsync(
        Guid entityId, string workQid, string? workLabel,
        string linkType = "appears_in", CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO fictional_entity_work_links
                (entity_id, work_qid, work_label, link_type)
            VALUES
                (@entityId, @workQid, @workLabel, @linkType);
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());
        cmd.Parameters.AddWithValue("@workQid", workQid);
        cmd.Parameters.AddWithValue("@workLabel", (object?)workLabel ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@linkType", linkType);

        cmd.ExecuteNonQuery();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>
        GetWorkLinksAsync(Guid entityId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT work_qid, work_label, link_type
            FROM   fictional_entity_work_links
            WHERE  entity_id = @entityId
            ORDER BY work_qid;
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());

        var result = new List<(string, string?, string)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            result.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2)
            ));
        }

        return Task.FromResult<IReadOnlyList<(string WorkQid, string? WorkLabel, string LinkType)>>(result);
    }

    /// <inheritdoc/>
    public Task<int> CountAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fictional_entities;";
        return Task.FromResult(Convert.ToInt32(cmd.ExecuteScalar()));
    }

    // ── Row Mapper ──────────────────────────────────────────────────────────

    private static FictionalEntity MapRow(SqliteDataReader r) => new()
    {
        Id                    = Guid.Parse(r.GetString(0)),
        WikidataQid           = r.GetString(1),
        Label                 = r.GetString(2),
        Description           = r.IsDBNull(3) ? null : r.GetString(3),
        EntitySubType         = r.GetString(4),
        FictionalUniverseQid  = r.IsDBNull(5) ? null : r.GetString(5),
        FictionalUniverseLabel = r.IsDBNull(6) ? null : r.GetString(6),
        ImageUrl              = r.IsDBNull(7) ? null : r.GetString(7),
        LocalImagePath        = r.IsDBNull(8) ? null : r.GetString(8),
        CreatedAt             = DateTimeOffset.Parse(r.GetString(9)),
        EnrichedAt            = r.IsDBNull(10) ? null : DateTimeOffset.Parse(r.GetString(10)),
    };
}
