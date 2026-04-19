using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services;

/// <summary>
/// One-time startup reconciliation that moves legacy artwork into the central asset tree.
/// </summary>
public sealed class AssetStorageStartupService : IHostedService
{
    private readonly IDatabaseConnection _db;
    private readonly AssetPathService _assetPaths;
    private readonly IAssetExportService _assetExportService;
    private readonly ILogger<AssetStorageStartupService> _logger;

    public AssetStorageStartupService(
        IDatabaseConnection db,
        AssetPathService assetPaths,
        IAssetExportService assetExportService,
        ILogger<AssetStorageStartupService> logger)
    {
        _db = db;
        _assetPaths = assetPaths;
        _assetExportService = assetExportService;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureAssetDirectories();
        ReconcileEntityAssetFiles(cancellationToken);
        ReconcilePersonHeadshots(cancellationToken);
        ReconcileCharacterPortraitFiles(cancellationToken);
        await _assetExportService.ReconcileAllArtworkAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void EnsureAssetDirectories()
    {
        Directory.CreateDirectory(_assetPaths.AssetsRoot);
        Directory.CreateDirectory(_assetPaths.ArtworkRoot);
        Directory.CreateDirectory(_assetPaths.DerivedRoot);
        Directory.CreateDirectory(_assetPaths.MetadataRoot);
        Directory.CreateDirectory(_assetPaths.TranscriptsRoot);
        Directory.CreateDirectory(_assetPaths.SubtitleCacheRoot);
        Directory.CreateDirectory(_assetPaths.PeopleRoot);
    }

    private void ReconcileEntityAssetFiles(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, entity_id, entity_type, asset_type, local_image_path
            FROM entity_assets
            WHERE local_image_path IS NOT NULL
              AND local_image_path <> '';
            """;

        using var reader = cmd.ExecuteReader();
        var rows = new List<AssetStorageRow>();
        while (reader.Read())
        {
            rows.Add(new AssetStorageRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            if (!Guid.TryParse(row.Id, out var variantId))
                continue;

            var extension = Path.GetExtension(row.LocalImagePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = string.Equals(row.AssetType, "Logo", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";

            var targetPath = _assetPaths.GetCentralAssetPath(
                row.EntityType,
                row.EntityId,
                row.AssetType,
                variantId,
                extension);

            var normalizedCurrent = NormalizePath(row.LocalImagePath);
            var normalizedTarget = NormalizePath(targetPath);
            if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                UpdateStorageRow(conn, row.Id, normalizedTarget, ownerScope: InferOwnerScope(row.AssetType), "Central");
                continue;
            }

            try
            {
                if (File.Exists(row.LocalImagePath))
                {
                    AssetPathService.EnsureDirectory(targetPath);

                    if (File.Exists(targetPath))
                        File.Delete(row.LocalImagePath);
                    else
                        File.Move(row.LocalImagePath, targetPath);

                    CleanEmptyParents(Path.GetDirectoryName(row.LocalImagePath), _assetPaths.LibraryRoot);
                }

                UpdateStorageRow(conn, row.Id, normalizedTarget, ownerScope: InferOwnerScope(row.AssetType), "Central");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Asset storage reconciliation failed for entity asset {VariantId}: {SourcePath} -> {TargetPath}",
                    row.Id,
                    row.LocalImagePath,
                    targetPath);
            }
        }
    }

    private void ReconcilePersonHeadshots(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, local_headshot_path
            FROM persons
            WHERE local_headshot_path IS NOT NULL
              AND local_headshot_path <> '';
            """;

        using var reader = cmd.ExecuteReader();
        var rows = new List<PersonHeadshotRow>();
        while (reader.Read())
        {
            rows.Add(new PersonHeadshotRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1)));
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(row.LocalHeadshotPath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            var targetPath = _assetPaths.GetPersonHeadshotPath(row.PersonId, extension);
            ReconcileFileMove(conn, "persons", "local_headshot_path", row.PersonId.ToString(), row.LocalHeadshotPath, targetPath);
        }
    }

    private void ReconcileCharacterPortraitFiles(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, person_id, fictional_entity_id, local_image_path
            FROM character_portraits
            WHERE local_image_path IS NOT NULL
              AND local_image_path <> '';
            """;

        using var reader = cmd.ExecuteReader();
        var rows = new List<CharacterPortraitStorageRow>();
        while (reader.Read())
        {
            rows.Add(new CharacterPortraitStorageRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3)));
        }

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(row.LocalImagePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            var targetPath = _assetPaths.GetCharacterPortraitPath(row.PersonId, row.FictionalEntityId, extension);
            ReconcileFileMove(conn, "character_portraits", "local_image_path", row.Id.ToString(), row.LocalImagePath, targetPath);
        }
    }

    private void ReconcileFileMove(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string tableName,
        string pathColumn,
        string rowId,
        string currentPath,
        string targetPath)
    {
        var normalizedCurrent = NormalizePath(currentPath);
        var normalizedTarget = NormalizePath(targetPath);
        if (string.Equals(normalizedCurrent, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            UpdatePathColumn(conn, tableName, pathColumn, rowId, normalizedTarget);
            return;
        }

        try
        {
            if (File.Exists(currentPath))
            {
                AssetPathService.EnsureDirectory(targetPath);

                if (File.Exists(targetPath))
                    File.Delete(currentPath);
                else
                    File.Move(currentPath, targetPath);

                CleanEmptyParents(Path.GetDirectoryName(currentPath), _assetPaths.LibraryRoot);
            }

            UpdatePathColumn(conn, tableName, pathColumn, rowId, normalizedTarget);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Asset storage reconciliation failed for {Table} row {RowId}: {SourcePath} -> {TargetPath}",
                tableName,
                rowId,
                currentPath,
                targetPath);
        }
    }

    private static void UpdateStorageRow(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string id,
        string localImagePath,
        string ownerScope,
        string storageLocation)
    {
        using var update = conn.CreateCommand();
        update.CommandText = """
            UPDATE entity_assets
            SET local_image_path = @path,
                asset_class = 'Artwork',
                storage_location = @storageLocation,
                owner_scope = @ownerScope,
                is_locally_exported = 0,
                is_preferred_exported = 0,
                updated_at = datetime('now')
            WHERE id = @id;
            """;
        update.Parameters.AddWithValue("@path", localImagePath);
        update.Parameters.AddWithValue("@storageLocation", storageLocation);
        update.Parameters.AddWithValue("@ownerScope", ownerScope);
        update.Parameters.AddWithValue("@id", id);
        update.ExecuteNonQuery();
    }

    private static void UpdatePathColumn(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        string tableName,
        string pathColumn,
        string id,
        string localImagePath)
    {
        using var update = conn.CreateCommand();
        update.CommandText = $"""
            UPDATE {tableName}
            SET {pathColumn} = @path
            WHERE id = @id;
            """;
        update.Parameters.AddWithValue("@path", localImagePath);
        update.Parameters.AddWithValue("@id", id);
        update.ExecuteNonQuery();
    }

    private static string InferOwnerScope(string assetType) =>
        assetType switch
        {
            "SeasonPoster" or "SeasonThumb" => "Season",
            "EpisodeStill" => "Episode",
            _ => "Work",
        };

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static void CleanEmptyParents(string? directory, string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        var current = new DirectoryInfo(directory);
        var root = new DirectoryInfo(Path.GetFullPath(libraryRoot));

        while (current.Exists
               && current.FullName.StartsWith(root.FullName, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(current.FullName, root.FullName, StringComparison.OrdinalIgnoreCase))
        {
            if (current.EnumerateFileSystemInfos().Any())
                break;

            var next = current.Parent;
            current.Delete();
            if (next is null)
                break;
            current = next;
        }
    }

    private sealed record AssetStorageRow(
        string Id,
        string EntityId,
        string EntityType,
        string AssetType,
        string LocalImagePath);

    private sealed record PersonHeadshotRow(Guid PersonId, string LocalHeadshotPath);

    private sealed record CharacterPortraitStorageRow(
        Guid Id,
        Guid PersonId,
        Guid FictionalEntityId,
        string LocalImagePath);
}
