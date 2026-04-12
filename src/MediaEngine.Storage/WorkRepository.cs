using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IWorkRepository"/>.
///
/// Phase 3 (M-082) replaced the legacy title+author dedup with parent/child
/// resolution. The repository now exposes:
///
/// <list type="bullet">
///   <item>Indexed find-or-create against the <c>parent_key</c> shadow column
///     for parent Works (albums, shows, series).</item>
///   <item>Ordinal/title lookups for child Works under a known parent.</item>
///   <item>Catalog row promotion when a previously unowned child gets a file.</item>
///   <item>Merging writes to the <c>external_identifiers</c> JSON blob.</item>
/// </list>
///
/// All inserts use parameterised SQL through <see cref="SqliteConnection"/>
/// directly (Dapper is used only for the lightweight queries).
/// </summary>
public sealed class WorkRepository : IWorkRepository
{
    private readonly IDatabaseConnection _db;
    private readonly ILogger<WorkRepository>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public WorkRepository(IDatabaseConnection db, ILogger<WorkRepository>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
        _logger = logger;
    }

    // ── Lookups ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Guid?> FindParentByKeyAsync(
        MediaType mediaType,
        string parentKey,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(parentKey))
            return Task.FromResult<Guid?>(null);

        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id
            FROM   works
            WHERE  media_type = @mediaType
              AND  parent_key = @parentKey
              AND  work_kind  = 'parent'
            LIMIT  1;
            """;

        var idStr = conn.QueryFirstOrDefault<string?>(
            sql, new { mediaType = mediaType.ToString(), parentKey });

        return Task.FromResult<Guid?>(idStr is null ? null : Guid.Parse(idStr));
    }

    /// <inheritdoc/>
    public Task<Guid?> FindChildByOrdinalAsync(
        Guid parentWorkId,
        int ordinal,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        const string sql = """
            SELECT id
            FROM   works
            WHERE  parent_work_id = @parentId
              AND  ordinal        = @ordinal
            LIMIT  1;
            """;

        var idStr = conn.QueryFirstOrDefault<string?>(
            sql, new { parentId = parentWorkId.ToString(), ordinal });

        return Task.FromResult<Guid?>(idStr is null ? null : Guid.Parse(idStr));
    }

    /// <inheritdoc/>
    public Task<Guid?> FindChildByTitleAsync(
        Guid parentWorkId,
        string title,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(title))
            return Task.FromResult<Guid?>(null);

        using var conn = _db.CreateConnection();

        // Title comparison goes through the canonical_values → media_assets →
        // editions chain. For catalog children that have no asset yet, we
        // also fall back to canonical values written directly with the
        // child's work id as entity_id (CatalogUpsertService).
        const string sql = """
            SELECT w.id
            FROM   works w
            LEFT   JOIN editions e        ON e.work_id      = w.id
            LEFT   JOIN media_assets ma   ON ma.edition_id  = e.id
            LEFT   JOIN canonical_values cv_asset
                    ON cv_asset.entity_id = ma.id AND cv_asset.key = 'title'
            LEFT   JOIN canonical_values cv_work
                    ON cv_work.entity_id  = w.id  AND cv_work.key  = 'title'
            WHERE  w.parent_work_id = @parentId
              AND  (cv_asset.value = @title COLLATE NOCASE
                 OR cv_work.value  = @title COLLATE NOCASE)
            LIMIT  1;
            """;

        var idStr = conn.QueryFirstOrDefault<string?>(
            sql, new { parentId = parentWorkId.ToString(), title });

        return Task.FromResult<Guid?>(idStr is null ? null : Guid.Parse(idStr));
    }

    /// <inheritdoc/>
    public Task<Guid?> FindByExternalIdentifierAsync(
        string scheme,
        string value,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(value))
            return Task.FromResult<Guid?>(null);

        using var conn = _db.CreateConnection();

        // SQLite's json_extract is the cleanest way to read a single key from
        // the JSON blob. The schemes we use are well-known constants
        // (BridgeIdKeys.*) so the path is always "$.{scheme}".
        const string sql = """
            SELECT id
            FROM   works
            WHERE  external_identifiers IS NOT NULL
              AND  json_extract(external_identifiers, '$.' || @scheme) = @value
            LIMIT  1;
            """;

        var idStr = conn.QueryFirstOrDefault<string?>(
            sql, new { scheme, value });

        return Task.FromResult<Guid?>(idStr is null ? null : Guid.Parse(idStr));
    }

    // ── Inserts ────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<Guid> InsertParentAsync(
        MediaType mediaType,
        string parentKey,
        Guid? grandparentWorkId,
        int? ordinal,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var workId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works
                (id, collection_id, media_type, work_kind, parent_work_id,
                 ordinal, is_catalog_only, parent_key, wikidata_status)
            VALUES
                (@id, NULL, @mediaType, 'parent', @parentId,
                 @ordinal, 0, @parentKey, 'pending');
            """;
        cmd.Parameters.AddWithValue("@id",         workId.ToString());
        cmd.Parameters.AddWithValue("@mediaType",  mediaType.ToString());
        cmd.Parameters.AddWithValue("@parentId",   (object?)grandparentWorkId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ordinal",    (object?)ordinal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@parentKey",  parentKey);
        cmd.ExecuteNonQuery();

        _logger?.LogDebug(
            "Inserted parent Work {WorkId} ({MediaType}) parent_key='{ParentKey}' grandparent={Grandparent} ordinal={Ordinal}",
            workId, mediaType, parentKey, grandparentWorkId, ordinal);

        return Task.FromResult(workId);
    }

    /// <inheritdoc/>
    public Task<Guid> InsertChildAsync(
        MediaType mediaType,
        Guid parentWorkId,
        int? ordinal,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var workId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works
                (id, collection_id, media_type, work_kind, parent_work_id,
                 ordinal, is_catalog_only, wikidata_status)
            VALUES
                (@id, NULL, @mediaType, 'child', @parentId,
                 @ordinal, 0, 'pending');
            """;
        cmd.Parameters.AddWithValue("@id",        workId.ToString());
        cmd.Parameters.AddWithValue("@mediaType", mediaType.ToString());
        cmd.Parameters.AddWithValue("@parentId",  parentWorkId.ToString());
        cmd.Parameters.AddWithValue("@ordinal",   (object?)ordinal ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.FromResult(workId);
    }

    /// <inheritdoc/>
    public Task<Guid> InsertStandaloneAsync(
        MediaType mediaType,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var workId = Guid.NewGuid();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works
                (id, collection_id, media_type, work_kind, is_catalog_only, wikidata_status)
            VALUES
                (@id, NULL, @mediaType, 'standalone', 0, 'pending');
            """;
        cmd.Parameters.AddWithValue("@id",        workId.ToString());
        cmd.Parameters.AddWithValue("@mediaType", mediaType.ToString());
        cmd.ExecuteNonQuery();

        return Task.FromResult(workId);
    }

    /// <inheritdoc/>
    public Task<Guid> InsertCatalogChildAsync(
        MediaType mediaType,
        Guid parentWorkId,
        int? ordinal,
        IReadOnlyDictionary<string, string>? externalIdentifiers,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var workId = Guid.NewGuid();

        var idsJson = externalIdentifiers is { Count: > 0 }
            ? JsonSerializer.Serialize(externalIdentifiers, JsonOptions)
            : null;

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO works
                (id, collection_id, media_type, work_kind, parent_work_id,
                 ordinal, is_catalog_only, external_identifiers, wikidata_status,
                 ownership)
            VALUES
                (@id, NULL, @mediaType, 'catalog', @parentId,
                 @ordinal, 1, @ids, 'pending',
                 'Unowned');
            """;
        cmd.Parameters.AddWithValue("@id",        workId.ToString());
        cmd.Parameters.AddWithValue("@mediaType", mediaType.ToString());
        cmd.Parameters.AddWithValue("@parentId",  parentWorkId.ToString());
        cmd.Parameters.AddWithValue("@ordinal",   (object?)ordinal ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ids",       (object?)idsJson ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.FromResult(workId);
    }

    // ── Mutations ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task PromoteCatalogToOwnedAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE works
            SET    work_kind       = 'child',
                   is_catalog_only = 0,
                   ownership       = 'Owned'
            WHERE  id              = @id
              AND  work_kind       = 'catalog';
            """;
        cmd.Parameters.AddWithValue("@id", workId.ToString());
        var rows = cmd.ExecuteNonQuery();

        if (rows > 0)
            _logger?.LogInformation("Promoted catalog Work {WorkId} to owned child", workId);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task WriteExternalIdentifiersAsync(
        Guid workId,
        IReadOnlyDictionary<string, string> identifiers,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (identifiers.Count == 0) return Task.CompletedTask;

        using var conn = _db.CreateConnection();

        // Read existing blob, merge new keys (no overwrite), write back.
        // Done in a single transaction to avoid lost-update races between
        // RetailMatchWorker and WikidataBridgeWorker writing in parallel.
        using var tx = conn.BeginTransaction();

        string? currentJson;
        using (var read = conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT external_identifiers FROM works WHERE id = @id;";
            read.Parameters.AddWithValue("@id", workId.ToString());
            currentJson = read.ExecuteScalar() as string;
        }

        Dictionary<string, string> merged;
        if (string.IsNullOrWhiteSpace(currentJson))
        {
            merged = new Dictionary<string, string>(identifiers, StringComparer.OrdinalIgnoreCase);
        }
        else
        {
            try
            {
                merged = JsonSerializer.Deserialize<Dictionary<string, string>>(currentJson)
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                _logger?.LogWarning(
                    "Existing external_identifiers JSON for Work {WorkId} is malformed; resetting",
                    workId);
                merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var kv in identifiers)
            {
                if (!merged.ContainsKey(kv.Key))
                    merged[kv.Key] = kv.Value;
            }
        }

        var newJson = JsonSerializer.Serialize(merged, JsonOptions);

        using (var write = conn.CreateCommand())
        {
            write.Transaction = tx;
            write.CommandText = """
                UPDATE works
                SET    external_identifiers = @json
                WHERE  id                   = @id;
                """;
            write.Parameters.AddWithValue("@id",   workId.ToString());
            write.Parameters.AddWithValue("@json", newJson);
            write.ExecuteNonQuery();
        }

        tx.Commit();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<WorkLineage?> GetLineageByAssetAsync(
        Guid assetId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // Single join walks asset → edition → work plus two LEFT JOINs up
        // the parent chain. COALESCE picks the topmost parent so a TV episode
        // resolves to its SHOW (gp.id) not its season (p.id), and a music
        // track resolves to its album (p.id), and a standalone movie falls
        // back to its own Work (w.id).
        const string sql = """
            SELECT a.id             AS AssetId,
                   e.id             AS EditionId,
                   w.id             AS WorkId,
                   w.parent_work_id AS ParentWorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootParentWorkId,
                   w.work_kind      AS WorkKind,
                   w.media_type     AS MediaType
            FROM   media_assets a
            JOIN   editions e  ON e.id  = a.edition_id
            JOIN   works    w  ON w.id  = e.work_id
            LEFT JOIN works p  ON p.id  = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE  a.id = @assetId
            LIMIT  1;
            """;

        using var conn = _db.CreateConnection();
        var row = conn.QueryFirstOrDefault<LineageRow>(sql, new { assetId = assetId.ToString() });
        if (row is null)
            return Task.FromResult<WorkLineage?>(null);

        var workKind = Enum.TryParse<WorkKind>(
            row.WorkKind, ignoreCase: true, out var wk)
            ? wk
            : WorkKind.Standalone;

        var mediaType = Enum.TryParse<MediaType>(row.MediaType, ignoreCase: true, out var mt)
            ? mt
            : MediaType.Unknown;

        var lineage = new WorkLineage(
            AssetId:          Guid.Parse(row.AssetId),
            EditionId:        Guid.Parse(row.EditionId),
            WorkId:           Guid.Parse(row.WorkId),
            ParentWorkId:     string.IsNullOrEmpty(row.ParentWorkId) ? null : Guid.Parse(row.ParentWorkId),
            RootParentWorkId: Guid.Parse(row.RootParentWorkId),
            WorkKind:         workKind,
            MediaType:        mediaType);

        return Task.FromResult<WorkLineage?>(lineage);
    }

    private sealed class LineageRow
    {
        public string AssetId          { get; set; } = string.Empty;
        public string EditionId        { get; set; } = string.Empty;
        public string WorkId           { get; set; } = string.Empty;
        public string? ParentWorkId    { get; set; }
        public string RootParentWorkId { get; set; } = string.Empty;
        public string WorkKind         { get; set; } = string.Empty;
        public string MediaType        { get; set; } = string.Empty;
    }
}
