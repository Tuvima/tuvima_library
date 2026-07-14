using System.Data;
using System.Text.Json;
using Dapper;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

public interface IMetadataEndpointDataService
{
    Task<MetadataReclassifyTarget> ResolveReclassifyTargetAsync(Guid entityId, CancellationToken ct = default);
    Task UpdateWorkMediaTypeAsync(Guid workId, string mediaType, CancellationToken ct = default);
    Task<MetadataEditorLaunchContext?> ResolveEditorLaunchAsync(Guid entityId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> GetDisplayOverridesAsync(Guid workId, CancellationToken ct = default);
    Task<Guid?> ResolveArtistArtworkOwnerAsync(
        Guid? representativeAssetId,
        string? artistName,
        CancellationToken ct = default);
    Task<Guid?> ResolveRepresentativeAssetAsync(
        IReadOnlyCollection<Guid> candidateWorkIds,
        CancellationToken ct = default);
    Task<MetadataArtworkResolutionContext> ResolveArtworkContextAsync(Guid entityId, CancellationToken ct = default);
}

/// <summary>
/// Typed SQL boundary for the metadata editor. It centralizes entity/work/asset
/// resolution shared by editor scope and artwork routes.
/// </summary>
public sealed class MetadataEndpointDataService(IDatabaseConnection db) : IMetadataEndpointDataService
{
    public async Task<MetadataReclassifyTarget> ResolveReclassifyTargetAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        var row = await connection.QueryFirstOrDefaultAsync<ReclassifyRow>(new CommandDefinition("""
            SELECT AssetId, WorkId
            FROM (
                SELECT ma.id AS AssetId, e.work_id AS WorkId, 0 AS Priority
                FROM media_assets ma
                INNER JOIN editions e ON e.id = ma.edition_id
                WHERE ma.id = @entityId

                UNION ALL

                SELECT ma.id AS AssetId, w.id AS WorkId, 1 AS Priority
                FROM works w
                LEFT JOIN editions e ON e.work_id = w.id
                LEFT JOIN media_assets ma ON ma.edition_id = e.id
                WHERE w.id = @entityId
            ) candidates
            ORDER BY Priority, AssetId IS NULL, AssetId
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        return row is null
            ? new MetadataReclassifyTarget(entityId, null)
            : new MetadataReclassifyTarget(row.AssetId ?? entityId, row.WorkId);
    }

    public async Task UpdateWorkMediaTypeAsync(
        Guid workId,
        string mediaType,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE works SET media_type = @mediaType WHERE id = @workId;",
            new { workId, mediaType },
            cancellationToken: ct));
    }

    public async Task<MetadataEditorLaunchContext?> ResolveEditorLaunchAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        var workRow = await connection.QueryFirstOrDefaultAsync<EditorLaunchWorkRow>(new CommandDefinition("""
            SELECT w.id AS WorkId,
                   w.media_type AS MediaType,
                   w.work_kind AS WorkKind,
                   w.parent_work_id AS ParentWorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        if (workRow is not null)
        {
            var sample = await GetRepresentativeAssetForWorkTreeAsync(connection, workRow.WorkId, ct);
            return MapLaunch(
                entityId,
                "Work",
                workRow.WorkId,
                workRow.ParentWorkId,
                workRow.RootWorkId,
                workRow.MediaType,
                workRow.WorkKind,
                sample);
        }

        var collectionRow = await connection.QueryFirstOrDefaultAsync<EditorLaunchCollectionRow>(new CommandDefinition("""
            SELECT target.id AS WorkId,
                   target.media_type AS MediaType,
                   target.work_kind AS WorkKind,
                   target.parent_work_id AS ParentWorkId,
                   target.id AS RootWorkId
            FROM collections c
            INNER JOIN works w ON w.collection_id = c.id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            INNER JOIN works target ON target.id = COALESCE(gp.id, p.id, w.id)
            WHERE c.id = @entityId
            ORDER BY CASE WHEN target.id = w.id THEN 0 ELSE 1 END,
                     COALESCE(w.ordinal, 999999),
                     w.id
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        if (collectionRow is not null)
        {
            var sample = await GetRepresentativeAssetForWorkTreeAsync(connection, collectionRow.WorkId, ct);
            return MapLaunch(
                entityId,
                "Collection",
                collectionRow.WorkId,
                collectionRow.ParentWorkId,
                collectionRow.RootWorkId,
                collectionRow.MediaType,
                collectionRow.WorkKind,
                sample);
        }

        var assetRow = await connection.QueryFirstOrDefaultAsync<EditorLaunchAssetRow>(new CommandDefinition("""
            SELECT a.id AS AssetId,
                   a.file_path_root AS FilePath,
                   a.writeback_status AS WritebackStatus,
                   w.id AS WorkId,
                   w.media_type AS MediaType,
                   w.work_kind AS WorkKind,
                   w.parent_work_id AS ParentWorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM media_assets a
            INNER JOIN editions e ON e.id = a.edition_id
            INNER JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE a.id = @entityId
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        return assetRow is null
            ? null
            : new MetadataEditorLaunchContext(
                entityId,
                "MediaAsset",
                assetRow.WorkId,
                assetRow.ParentWorkId,
                assetRow.RootWorkId ?? assetRow.WorkId,
                DefaultMediaType(assetRow.MediaType),
                DefaultWorkKind(assetRow.WorkKind),
                assetRow.AssetId,
                assetRow.FilePath,
                assetRow.WritebackStatus);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetDisplayOverridesAsync(
        Guid workId,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        var json = await connection.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT display_overrides_json FROM works WHERE id = @workId LIMIT 1;",
            new { workId },
            cancellationToken: ct));

        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task<Guid?> ResolveArtistArtworkOwnerAsync(
        Guid? representativeAssetId,
        string? artistName,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        if (representativeAssetId.HasValue)
        {
            var linkedId = await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
                SELECT p.id
                FROM person_media_links pml
                INNER JOIN persons p ON p.id = pml.person_id
                WHERE pml.media_asset_id = @assetId
                  AND (
                        pml.role IN ('Artist', 'Performer')
                        OR EXISTS (
                            SELECT 1
                            FROM person_roles pr
                            WHERE pr.person_id = p.id
                              AND pr.role IN ('Artist', 'Performer')
                        )
                  )
                ORDER BY CASE
                    WHEN pml.role = 'Artist' THEN 0
                    WHEN pml.role = 'Performer' THEN 1
                    ELSE 2
                END,
                p.name
                LIMIT 1;
                """, new { assetId = representativeAssetId.Value }, cancellationToken: ct));
            if (linkedId.HasValue)
                return linkedId;
        }

        if (string.IsNullOrWhiteSpace(artistName))
            return null;

        return await connection.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition("""
            SELECT p.id
            FROM persons p
            WHERE p.name = @artistName COLLATE NOCASE
            ORDER BY p.name
            LIMIT 1;
            """, new { artistName = artistName.Trim() }, cancellationToken: ct));
    }

    public async Task<Guid?> ResolveRepresentativeAssetAsync(
        IReadOnlyCollection<Guid> candidateWorkIds,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        foreach (var workId in candidateWorkIds.Where(id => id != Guid.Empty).Distinct())
        {
            var sample = await GetRepresentativeAssetForWorkTreeAsync(connection, workId, ct);
            if (sample is not null)
                return sample.AssetId;
        }

        return null;
    }

    public async Task<MetadataArtworkResolutionContext> ResolveArtworkContextAsync(
        Guid entityId,
        CancellationToken ct = default)
    {
        using var connection = db.CreateConnection();
        var workRow = await connection.QueryFirstOrDefaultAsync<ArtworkWorkResolutionRow>(new CommandDefinition("""
            SELECT w.id AS WorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   (
                       SELECT ma_current.id
                       FROM editions e_current
                       INNER JOIN media_assets ma_current ON ma_current.edition_id = e_current.id
                       WHERE e_current.work_id = w.id
                       ORDER BY ma_current.id
                       LIMIT 1
                   ) AS PrimaryAssetId,
                   (
                       SELECT ma_root.id
                       FROM editions e_root
                       INNER JOIN media_assets ma_root ON ma_root.edition_id = e_root.id
                       WHERE e_root.work_id = COALESCE(gp.id, p.id, w.id)
                       ORDER BY ma_root.id
                       LIMIT 1
                   ) AS RootPrimaryAssetId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        if (workRow is not null)
        {
            var ids = await GetArtworkEntityIdsAsync(connection, workRow.WorkId, workRow.RootWorkId, ct);
            return BuildArtworkContext(
                entityId,
                workRow.WorkId,
                workRow.RootWorkId,
                workRow.PrimaryAssetId,
                workRow.RootPrimaryAssetId,
                ids);
        }

        var assetRow = await connection.QueryFirstOrDefaultAsync<ArtworkAssetResolutionRow>(new CommandDefinition("""
            SELECT a.id AS AssetId,
                   w.id AS WorkId,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                   (
                       SELECT ma_root.id
                       FROM editions e_root
                       INNER JOIN media_assets ma_root ON ma_root.edition_id = e_root.id
                       WHERE e_root.work_id = COALESCE(gp.id, p.id, w.id)
                       ORDER BY ma_root.id
                       LIMIT 1
                   ) AS RootPrimaryAssetId
            FROM media_assets a
            INNER JOIN editions e ON e.id = a.edition_id
            INNER JOIN works w ON w.id = e.work_id
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE a.id = @entityId
            LIMIT 1;
            """, new { entityId }, cancellationToken: ct));

        if (assetRow is null)
        {
            return new MetadataArtworkResolutionContext(
                entityId, null, null, null, null, [entityId], null);
        }

        var artworkIds = await GetArtworkEntityIdsAsync(connection, assetRow.WorkId, assetRow.RootWorkId, ct);
        if (!artworkIds.Contains(entityId))
            artworkIds.Insert(0, entityId);

        return BuildArtworkContext(
            entityId,
            assetRow.WorkId,
            assetRow.RootWorkId,
            assetRow.AssetId,
            assetRow.RootPrimaryAssetId,
            artworkIds);
    }

    private static async Task<MetadataEditorAssetSample?> GetRepresentativeAssetForWorkTreeAsync(
        IDbConnection connection,
        Guid workId,
        CancellationToken ct)
    {
        return await connection.QueryFirstOrDefaultAsync<MetadataEditorAssetSample>(new CommandDefinition("""
            WITH RECURSIVE work_tree(id, depth) AS (
                SELECT @workId, 0
                UNION ALL
                SELECT child.id, work_tree.depth + 1
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.id
            )
            SELECT ma.id AS AssetId,
                   ma.file_path_root AS FilePath,
                   ma.writeback_status AS WritebackStatus
            FROM work_tree
            INNER JOIN editions e ON e.work_id = work_tree.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            ORDER BY work_tree.depth, ma.id
            LIMIT 1;
            """, new { workId }, cancellationToken: ct));
    }

    private static async Task<List<Guid>> GetArtworkEntityIdsAsync(
        IDbConnection connection,
        Guid? workId,
        Guid? rootWorkId,
        CancellationToken ct)
    {
        var ids = new List<Guid>();
        AddId(ids, workId);
        AddId(ids, rootWorkId);
        if (ids.Count == 0)
            return ids;

        var assetRows = await connection.QueryAsync<Guid>(new CommandDefinition("""
            SELECT DISTINCT ma.id
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id IN @workIds;
            """, new { workIds = ids.Select(GuidSql.ToBlob).ToArray() }, cancellationToken: ct));
        foreach (var assetId in assetRows)
            AddId(ids, assetId);
        return ids;
    }

    private static MetadataEditorLaunchContext MapLaunch(
        Guid launchEntityId,
        string launchEntityKind,
        Guid workId,
        Guid? parentWorkId,
        Guid? rootWorkId,
        string? mediaType,
        string? workKind,
        MetadataEditorAssetSample? sample) =>
        new(
            launchEntityId,
            launchEntityKind,
            workId,
            parentWorkId,
            rootWorkId ?? workId,
            DefaultMediaType(mediaType),
            DefaultWorkKind(workKind),
            sample?.AssetId,
            sample?.FilePath,
            sample?.WritebackStatus);

    private static MetadataArtworkResolutionContext BuildArtworkContext(
        Guid requestedEntityId,
        Guid? workId,
        Guid? rootWorkId,
        Guid? primaryAssetId,
        Guid? rootPrimaryAssetId,
        List<Guid> artworkEntityIds)
    {
        var resolvedRootWorkId = rootWorkId ?? workId;
        var dedupedIds = artworkEntityIds.Where(id => id != Guid.Empty).Distinct().ToList();
        AddId(dedupedIds, workId);
        AddId(dedupedIds, resolvedRootWorkId);
        return new MetadataArtworkResolutionContext(
            requestedEntityId,
            workId,
            resolvedRootWorkId,
            primaryAssetId,
            rootPrimaryAssetId,
            dedupedIds,
            primaryAssetId ?? rootPrimaryAssetId);
    }

    private static void AddId(ICollection<Guid> ids, Guid? id)
    {
        if (id.HasValue && id.Value != Guid.Empty && !ids.Contains(id.Value))
            ids.Add(id.Value);
    }

    private static string DefaultMediaType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Books" : value;

    private static string DefaultWorkKind(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "standalone" : value;

    private sealed class ReclassifyRow
    {
        public Guid? AssetId { get; init; }
        public Guid WorkId { get; init; }
    }

    private sealed record EditorLaunchWorkRow(
        Guid WorkId,
        string? MediaType,
        string? WorkKind,
        Guid? ParentWorkId,
        Guid? RootWorkId);
    private sealed record EditorLaunchCollectionRow(
        Guid WorkId,
        string? MediaType,
        string? WorkKind,
        Guid? ParentWorkId,
        Guid? RootWorkId);
    private sealed record EditorLaunchAssetRow(
        Guid AssetId,
        string? FilePath,
        string? WritebackStatus,
        Guid WorkId,
        string? MediaType,
        string? WorkKind,
        Guid? ParentWorkId,
        Guid? RootWorkId);
    private sealed record ArtworkWorkResolutionRow(
        Guid WorkId,
        Guid RootWorkId,
        Guid? PrimaryAssetId,
        Guid? RootPrimaryAssetId);
    private sealed record ArtworkAssetResolutionRow(
        Guid AssetId,
        Guid WorkId,
        Guid RootWorkId,
        Guid? RootPrimaryAssetId);
}

public sealed record MetadataReclassifyTarget(Guid TargetAssetId, Guid? WorkId);

public sealed record MetadataEditorAssetSample(
    Guid AssetId,
    string? FilePath,
    string? WritebackStatus);

public sealed record MetadataEditorLaunchContext(
    Guid LaunchEntityId,
    string LaunchEntityKind,
    Guid WorkId,
    Guid? ParentWorkId,
    Guid RootWorkId,
    string MediaType,
    string WorkKind,
    Guid? RepresentativeAssetId,
    string? RepresentativeMediaFilePath,
    string? RepresentativeWritebackStatus);

public sealed record MetadataArtworkResolutionContext(
    Guid RequestedEntityId,
    Guid? WorkId,
    Guid? RootWorkId,
    Guid? PrimaryAssetId,
    Guid? RootPrimaryAssetId,
    IReadOnlyList<Guid> ArtworkEntityIds,
    Guid? PreferredArtworkEntityId);
