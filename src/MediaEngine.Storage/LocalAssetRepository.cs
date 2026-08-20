using System.Text.Json;
using Dapper;
using MediaEngine.Contracts.LocalAssets;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

/// <summary>
/// Stores personal-library items independently from catalogue identities.
/// Logical items, hash-addressed files, and observed source paths are separate
/// so compound assets and exact duplicates retain truthful provenance.
/// </summary>
public sealed class LocalAssetRepository(IDatabaseConnection database) : ILocalAssetRepository
{
    private sealed class ItemRow
    {
        public Guid Id { get; init; }
        public Guid LibraryId { get; init; }
        public string MediaKind { get; init; } = string.Empty;
        public string? Title { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public DateTimeOffset? CapturedAt { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public double? DurationSeconds { get; init; }
        public int? PageCount { get; init; }
        public string? DeviceMake { get; init; }
        public string? DeviceModel { get; init; }
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string? LocationName { get; init; }
        public long Favorite { get; init; }
        public long Hidden { get; init; }
        public long SourceCount { get; init; }
        public long TotalCount { get; init; }
    }

    private sealed class FileRow
    {
        public Guid Id { get; init; }
        public string Role { get; init; } = string.Empty;
        public string? DerivativeKind { get; init; }
        public string MimeType { get; init; } = string.Empty;
        public long ByteSize { get; init; }
        public long SourceCount { get; init; }
    }

    private sealed class ContentRow
    {
        public Guid ItemId { get; init; }
        public Guid FileId { get; init; }
        public Guid LibraryId { get; init; }
        public string FilePath { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public long ByteSize { get; init; }
        public string ContentHash { get; init; } = string.Empty;
        public string Role { get; init; } = string.Empty;
        public string? DerivativeKind { get; init; }
    }

    private sealed class CollectionRow
    {
        public Guid Id { get; init; }
        public Guid LibraryId { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string CollectionKind { get; init; } = string.Empty;
        public long ItemCount { get; init; }
        public Guid? CoverItemId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    private sealed class SearchDocumentRow
    {
        public string? Title { get; init; }
        public string FileName { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
        public string MediaKind { get; init; } = string.Empty;
        public string? CapturedAt { get; init; }
        public string? Device { get; init; }
        public string? Location { get; init; }
        public string? DocumentText { get; init; }
        public string? Tags { get; init; }
    }

    public LocalAssetPageDto Query(LocalAssetQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateQuery(query);
        ct.ThrowIfCancellationRequested();

        var mediaKinds = query.MediaKinds?
            .Where(kind => !string.IsNullOrWhiteSpace(kind))
            .Select(NormalizeMediaKind)
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
        var searchExpression = BuildSearchExpression(query.Search);

        using var connection = database.CreateConnection();
        var rows = connection.Query<ItemRow>("""
            SELECT li.id AS Id,
                   li.library_id AS LibraryId,
                   li.media_kind AS MediaKind,
                   li.title AS Title,
                   li.primary_file_name AS FileName,
                   li.primary_mime_type AS MimeType,
                   li.captured_at AS CapturedAt,
                   li.created_at AS CreatedAt,
                   lm.width AS Width,
                   lm.height AS Height,
                   lm.duration_seconds AS DurationSeconds,
                   lm.page_count AS PageCount,
                   lm.device_make AS DeviceMake,
                   lm.device_model AS DeviceModel,
                   lm.latitude AS Latitude,
                   lm.longitude AS Longitude,
                   lm.location_name AS LocationName,
                   li.favorite AS Favorite,
                   li.hidden AS Hidden,
                   (SELECT COUNT(DISTINCT lfs.id)
                      FROM local_item_files lif
                      JOIN local_file_sources lfs
                        ON lfs.file_id = lif.file_id AND lfs.library_id = li.library_id
                     WHERE lif.item_id = li.id) AS SourceCount,
                   COUNT(*) OVER() AS TotalCount
              FROM local_items li
              LEFT JOIN local_item_metadata lm ON lm.item_id = li.id
             WHERE li.library_id = @LibraryId
               AND ((@HiddenOnly = 1 AND li.hidden = 1)
                    OR (@HiddenOnly = 0 AND (@IncludeHidden = 1 OR li.hidden = 0)))
               AND (@FavoritesOnly = 0 OR li.favorite = 1)
               AND (@HasMediaKinds = 0 OR li.media_kind IN @MediaKinds)
               AND (@CollectionId IS NULL OR EXISTS (
                    SELECT 1 FROM local_collection_items lci
                     WHERE lci.collection_id = @CollectionId AND lci.item_id = li.id))
               AND (@SearchExpression IS NULL OR EXISTS (
                    SELECT 1 FROM local_item_search lis
                      JOIN local_item_search_keys lsk ON lsk.rowid = lis.rowid
                     WHERE lsk.item_id = li.id
                       AND local_item_search MATCH @SearchExpression))
             ORDER BY COALESCE(li.captured_at, li.created_at) DESC, li.id
             LIMIT @Limit OFFSET @Offset;
            """, new
        {
            query.LibraryId,
            query.Offset,
            query.Limit,
            HiddenOnly = query.HiddenOnly ? 1 : 0,
            IncludeHidden = query.IncludeHidden ? 1 : 0,
            FavoritesOnly = query.FavoritesOnly ? 1 : 0,
            HasMediaKinds = mediaKinds.Length == 0 ? 0 : 1,
            MediaKinds = mediaKinds.Length == 0 ? [LocalAssetMediaKinds.Other] : mediaKinds,
            query.CollectionId,
            SearchExpression = searchExpression,
        }).ToList();

        var items = rows.Select(row => MapItem(connection, row)).ToList();
        var total = rows.Count == 0 ? 0 : checked((int)rows[0].TotalCount);
        return new LocalAssetPageDto(
            items,
            query.Offset,
            query.Limit,
            total,
            query.Offset + items.Count < total);
    }

    public LocalAssetDto? Find(Guid itemId, CancellationToken ct = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<ItemRow>("""
            SELECT li.id AS Id, li.library_id AS LibraryId, li.media_kind AS MediaKind,
                   li.title AS Title, li.primary_file_name AS FileName,
                   li.primary_mime_type AS MimeType, li.captured_at AS CapturedAt,
                   li.created_at AS CreatedAt, lm.width AS Width, lm.height AS Height,
                   lm.duration_seconds AS DurationSeconds, lm.page_count AS PageCount,
                   lm.device_make AS DeviceMake, lm.device_model AS DeviceModel,
                   lm.latitude AS Latitude, lm.longitude AS Longitude,
                   lm.location_name AS LocationName, li.favorite AS Favorite,
                   li.hidden AS Hidden,
                   (SELECT COUNT(DISTINCT lfs.id)
                      FROM local_item_files lif
                      JOIN local_file_sources lfs
                        ON lfs.file_id = lif.file_id AND lfs.library_id = li.library_id
                     WHERE lif.item_id = li.id) AS SourceCount,
                   1 AS TotalCount
              FROM local_items li
              LEFT JOIN local_item_metadata lm ON lm.item_id = li.id
             WHERE li.id = @itemId;
            """, new { itemId });
        return row is null ? null : MapItem(connection, row);
    }

    public LocalAssetContentLocation? ResolveContent(
        Guid itemId,
        string role = LocalAssetFileRoles.Primary,
        CancellationToken ct = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        role = NormalizeRole(role);
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QueryFirstOrDefault<ContentRow>("""
            SELECT li.id AS ItemId, lf.id AS FileId, li.library_id AS LibraryId,
                   lfs.file_path AS FilePath, lf.mime_type AS MimeType,
                   lf.byte_size AS ByteSize, lf.content_hash AS ContentHash,
                   lif.role AS Role, lif.derivative_kind AS DerivativeKind
              FROM local_items li
              JOIN local_item_files lif ON lif.item_id = li.id
              JOIN local_files lf ON lf.id = lif.file_id
              JOIN local_file_sources lfs
                ON lfs.file_id = lf.id AND lfs.library_id = li.library_id
             WHERE li.id = @itemId AND lif.role = @role
             ORDER BY lfs.indexed_at DESC, lfs.id
             LIMIT 1;
            """, new { itemId, role });
        return row is null
            ? null
            : new LocalAssetContentLocation(
                row.ItemId,
                row.FileId,
                row.LibraryId,
                row.FilePath,
                row.MimeType,
                row.ByteSize,
                row.ContentHash,
                row.Role,
                row.DerivativeKind);
    }

    public IReadOnlyList<LocalCollectionDto> GetCollections(
        Guid libraryId,
        CancellationToken ct = default)
    {
        if (libraryId == Guid.Empty) throw new ArgumentException("Library ID is required.", nameof(libraryId));
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return connection.Query<CollectionRow>("""
            SELECT lc.id AS Id, lc.library_id AS LibraryId, lc.name AS Name,
                   lc.description AS Description, lc.collection_kind AS CollectionKind,
                   COUNT(lci.item_id) AS ItemCount,
                   (SELECT lci2.item_id FROM local_collection_items lci2
                     WHERE lci2.collection_id = lc.id
                     ORDER BY lci2.position, lci2.added_at LIMIT 1) AS CoverItemId,
                   lc.created_at AS CreatedAt
              FROM local_collections lc
              LEFT JOIN local_collection_items lci ON lci.collection_id = lc.id
             WHERE lc.library_id = @libraryId
             GROUP BY lc.id
             ORDER BY lc.modified_at DESC, lc.name COLLATE NOCASE;
            """, new { libraryId })
            .Select(MapCollection)
            .ToList();
    }

    public Task<LocalAssetUpsertResult> UpsertAsync(
        LocalAssetRegistration registration,
        CancellationToken ct = default)
    {
        ValidateRegistration(registration);
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var normalizedKind = NormalizeMediaKind(registration.MediaKind);
            var primary = registration.Files.SingleOrDefault(file =>
                string.Equals(file.Role, LocalAssetFileRoles.Primary, StringComparison.OrdinalIgnoreCase));

            var itemId = registration.ExistingItemId;
            if (itemId.HasValue)
            {
                var actualLibraryId = connection.QuerySingleOrDefault<Guid?>(
                    "SELECT library_id FROM local_items WHERE id = @itemId;",
                    new { itemId }, transaction);
                if (!actualLibraryId.HasValue)
                {
                    throw new InvalidOperationException($"Local item '{itemId:D}' does not exist.");
                }
                if (actualLibraryId.Value != registration.LibraryId)
                {
                    throw new InvalidOperationException("A local item cannot be attached across libraries.");
                }
            }
            else if (primary is not null)
            {
                var primaryHash = NormalizeHash(primary.ContentHash);
                itemId = connection.QuerySingleOrDefault<Guid?>("""
                    SELECT li.id
                      FROM local_items li
                      JOIN local_item_files lif ON lif.item_id = li.id AND lif.role = 'primary'
                      JOIN local_files lf ON lf.id = lif.file_id
                     WHERE li.library_id = @LibraryId AND lf.content_hash = @primaryHash COLLATE NOCASE
                     LIMIT 1;
                    """, new { registration.LibraryId, primaryHash }, transaction);
            }

            var itemAdded = !itemId.HasValue;
            itemId ??= Guid.NewGuid();
            if (itemAdded)
            {
                connection.Execute("""
                    INSERT INTO local_items
                        (id, library_id, media_kind, title, primary_file_name,
                         primary_mime_type, captured_at, created_at, updated_at, favorite, hidden)
                    VALUES
                        (@itemId, @LibraryId, @MediaKind, @Title, @FileName,
                         @MimeType, @CapturedAt, @now, @now, 0, 0);
                    """, new
                {
                    itemId,
                    registration.LibraryId,
                    MediaKind = normalizedKind,
                    registration.Title,
                    FileName = primary!.FileName.Trim(),
                    MimeType = primary.MimeType.Trim().ToLowerInvariant(),
                    registration.CapturedAt,
                    now,
                }, transaction);
            }
            else
            {
                connection.Execute("""
                    UPDATE local_items
                       SET title = COALESCE(@Title, title),
                           captured_at = COALESCE(@CapturedAt, captured_at),
                           updated_at = @now
                     WHERE id = @itemId;
                    """, new { itemId, registration.Title, registration.CapturedAt, now }, transaction);
            }

            connection.Execute("""
                INSERT INTO local_item_metadata
                    (item_id, width, height, duration_seconds, page_count, device_make,
                     device_model, latitude, longitude, location_name, document_text,
                     metadata_json, updated_at)
                VALUES
                    (@itemId, @Width, @Height, @DurationSeconds, @PageCount, @DeviceMake,
                     @DeviceModel, @Latitude, @Longitude, @LocationName, @DocumentText,
                     @MetadataJson, @now)
                ON CONFLICT(item_id) DO UPDATE SET
                    width = COALESCE(excluded.width, local_item_metadata.width),
                    height = COALESCE(excluded.height, local_item_metadata.height),
                    duration_seconds = COALESCE(excluded.duration_seconds, local_item_metadata.duration_seconds),
                    page_count = COALESCE(excluded.page_count, local_item_metadata.page_count),
                    device_make = COALESCE(excluded.device_make, local_item_metadata.device_make),
                    device_model = COALESCE(excluded.device_model, local_item_metadata.device_model),
                    latitude = COALESCE(excluded.latitude, local_item_metadata.latitude),
                    longitude = COALESCE(excluded.longitude, local_item_metadata.longitude),
                    location_name = COALESCE(excluded.location_name, local_item_metadata.location_name),
                    document_text = COALESCE(excluded.document_text, local_item_metadata.document_text),
                    metadata_json = COALESCE(excluded.metadata_json, local_item_metadata.metadata_json),
                    updated_at = excluded.updated_at;
                """, new
            {
                itemId,
                registration.Width,
                registration.Height,
                registration.DurationSeconds,
                registration.PageCount,
                registration.DeviceMake,
                registration.DeviceModel,
                registration.Latitude,
                registration.Longitude,
                registration.LocationName,
                registration.DocumentText,
                registration.MetadataJson,
                now,
            }, transaction);

            var filesAdded = 0;
            var sourcesAdded = 0;
            var position = 0;
            foreach (var file in registration.Files)
            {
                token.ThrowIfCancellationRequested();
                var hash = NormalizeHash(file.ContentHash);
                var role = NormalizeRole(file.Role);
                var fileId = connection.QuerySingleOrDefault<Guid?>(
                    "SELECT id FROM local_files WHERE content_hash = @hash COLLATE NOCASE;",
                    new { hash }, transaction);
                if (!fileId.HasValue)
                {
                    fileId = Guid.NewGuid();
                    filesAdded += connection.Execute("""
                        INSERT INTO local_files
                            (id, content_hash, byte_size, mime_type, extension, created_at)
                        VALUES (@fileId, @hash, @ByteSize, @MimeType, @Extension, @now);
                        """, new
                    {
                        fileId,
                        hash,
                        file.ByteSize,
                        MimeType = file.MimeType.Trim().ToLowerInvariant(),
                        Extension = Path.GetExtension(file.FileName).ToLowerInvariant(),
                        now,
                    }, transaction);
                }

                var sourceExists = connection.ExecuteScalar<long>("""
                    SELECT COUNT(*) FROM local_file_sources
                     WHERE library_id = @LibraryId AND file_path = @FilePath COLLATE NOCASE;
                    """, new { registration.LibraryId, file.FilePath }, transaction) != 0;
                connection.Execute("""
                    INSERT INTO local_file_sources
                        (id, file_id, library_id, file_path, modified_at, indexed_at)
                    VALUES (@sourceId, @fileId, @LibraryId, @FilePath, @ModifiedAt, @now)
                    ON CONFLICT(library_id, file_path COLLATE NOCASE) DO UPDATE SET
                        file_id = excluded.file_id,
                        modified_at = excluded.modified_at,
                        indexed_at = excluded.indexed_at;
                    """, new
                {
                    sourceId = Guid.NewGuid(),
                    fileId,
                    registration.LibraryId,
                    file.FilePath,
                    file.ModifiedAt,
                    now,
                }, transaction);
                if (!sourceExists) sourcesAdded++;

                connection.Execute("""
                    INSERT INTO local_item_files
                        (item_id, file_id, role, derivative_kind, position, added_at)
                    VALUES (@itemId, @fileId, @role, @DerivativeKind, @position, @now)
                    ON CONFLICT(item_id, file_id, role) DO UPDATE SET
                        derivative_kind = excluded.derivative_kind,
                        position = excluded.position;
                    """, new
                {
                    itemId,
                    fileId,
                    role,
                    file.DerivativeKind,
                    position,
                    now,
                }, transaction);
                position++;
            }

            if (registration.Tags is not null)
            {
                ReplaceTags(connection, transaction, itemId.Value, registration.Tags, now);
            }
            RebuildSearchDocument(connection, transaction, itemId.Value);
            return new LocalAssetUpsertResult(itemId.Value, itemAdded, filesAdded, sourcesAdded);
        }, ct);
    }

    public Task<bool> SetFlagsAsync(
        Guid itemId,
        bool? favorite,
        bool? hidden,
        CancellationToken ct = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        if (!favorite.HasValue && !hidden.HasValue)
        {
            throw new ArgumentException("At least one flag value is required.");
        }
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            return connection.Execute("""
                UPDATE local_items
                   SET favorite = COALESCE(@Favorite, favorite),
                       hidden = COALESCE(@Hidden, hidden),
                       updated_at = @now
                 WHERE id = @itemId;
                """, new
            {
                itemId,
                Favorite = favorite.HasValue ? (favorite.Value ? 1 : 0) : (int?)null,
                Hidden = hidden.HasValue ? (hidden.Value ? 1 : 0) : (int?)null,
                now = DateTimeOffset.UtcNow,
            }, transaction) > 0;
        }, ct);
    }

    public Task ReplaceTagsAsync(
        Guid itemId,
        IReadOnlyCollection<string> tags,
        CancellationToken ct = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        ArgumentNullException.ThrowIfNull(tags);
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (connection.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM local_items WHERE id = @itemId;",
                    new { itemId }, transaction) == 0)
            {
                throw new InvalidOperationException($"Local item '{itemId:D}' does not exist.");
            }
            ReplaceTags(connection, transaction, itemId, tags, DateTimeOffset.UtcNow);
            RebuildSearchDocument(connection, transaction, itemId);
        }, ct);
    }

    public Task<LocalCollectionDto> CreateCollectionAsync(
        Guid libraryId,
        string name,
        string? description,
        string collectionKind,
        CancellationToken ct = default)
    {
        if (libraryId == Guid.Empty) throw new ArgumentException("Library ID is required.", nameof(libraryId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        collectionKind = NormalizeCollectionKind(collectionKind);
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            connection.Execute("""
                INSERT INTO local_collections
                    (id, library_id, name, description, collection_kind, created_at, modified_at)
                VALUES (@id, @libraryId, @name, @description, @collectionKind, @now, @now);
                """, new
            {
                id,
                libraryId,
                name = name.Trim(),
                description = NullIfWhiteSpace(description),
                collectionKind,
                now,
            }, transaction);
            return new LocalCollectionDto(
                id, libraryId, name.Trim(), NullIfWhiteSpace(description),
                collectionKind, 0, null, now);
        }, ct);
    }

    public Task<int> AddToCollectionAsync(
        Guid collectionId,
        IReadOnlyCollection<Guid> itemIds,
        CancellationToken ct = default)
    {
        if (collectionId == Guid.Empty) throw new ArgumentException("Collection ID is required.", nameof(collectionId));
        ArgumentNullException.ThrowIfNull(itemIds);
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var libraryId = connection.QuerySingleOrDefault<Guid?>(
                "SELECT library_id FROM local_collections WHERE id = @collectionId;",
                new { collectionId }, transaction)
                ?? throw new InvalidOperationException($"Local collection '{collectionId:D}' does not exist.");
            var position = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(position), -1) + 1 FROM local_collection_items WHERE collection_id = @collectionId;",
                new { collectionId }, transaction);
            var now = DateTimeOffset.UtcNow;
            var added = 0;
            foreach (var itemId in itemIds.Where(id => id != Guid.Empty).Distinct())
            {
                token.ThrowIfCancellationRequested();
                added += connection.Execute("""
                    INSERT OR IGNORE INTO local_collection_items
                        (collection_id, item_id, position, added_at)
                    SELECT @collectionId, @itemId, @position, @now
                     WHERE EXISTS (
                        SELECT 1 FROM local_items
                         WHERE id = @itemId AND library_id = @libraryId);
                    """, new { collectionId, itemId, position, now, libraryId }, transaction);
                position++;
            }
            connection.Execute(
                "UPDATE local_collections SET modified_at = @now WHERE id = @collectionId;",
                new { collectionId, now }, transaction);
            return added;
        }, ct);
    }

    public Task<Guid> AddAnnotationAsync(
        Guid itemId,
        LocalAssetAnnotation annotation,
        CancellationToken ct = default)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item ID is required.", nameof(itemId));
        ValidateAnnotation(annotation);
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            if (connection.ExecuteScalar<long>(
                    "SELECT COUNT(*) FROM local_items WHERE id = @itemId;",
                    new { itemId }, transaction) == 0)
            {
                throw new InvalidOperationException($"Local item '{itemId:D}' does not exist.");
            }
            var id = Guid.NewGuid();
            connection.Execute("""
                INSERT INTO local_item_annotations
                    (id, item_id, annotation_kind, annotation_value, confidence, source,
                     model_name, model_version, provenance_json, created_at, reviewed_at)
                VALUES
                    (@id, @itemId, @Kind, @Value, @Confidence, @Source,
                     @ModelName, @ModelVersion, @ProvenanceJson, @now, @ReviewedAt);
                """, new
            {
                id,
                itemId,
                Kind = annotation.Kind.Trim(),
                Value = annotation.Value.Trim(),
                Source = annotation.Source.Trim(),
                annotation.Confidence,
                annotation.ModelName,
                annotation.ModelVersion,
                annotation.ProvenanceJson,
                now = DateTimeOffset.UtcNow,
                annotation.ReviewedAt,
            }, transaction);
            return id;
        }, ct);
    }

    private static LocalAssetDto MapItem(SqliteConnection connection, ItemRow row)
    {
        var files = connection.Query<FileRow>("""
            SELECT lf.id AS Id, lif.role AS Role, lif.derivative_kind AS DerivativeKind,
                   lf.mime_type AS MimeType, lf.byte_size AS ByteSize,
                   COUNT(lfs.id) AS SourceCount
              FROM local_item_files lif
              JOIN local_files lf ON lf.id = lif.file_id
              LEFT JOIN local_file_sources lfs
                ON lfs.file_id = lf.id AND lfs.library_id = @libraryId
             WHERE lif.item_id = @itemId
             GROUP BY lf.id, lif.role
             ORDER BY lif.position, lif.role;
            """, new { itemId = row.Id, libraryId = row.LibraryId })
            .Select(file => new LocalAssetFileDto(
                file.Id,
                file.Role,
                file.DerivativeKind,
                file.MimeType,
                file.ByteSize,
                checked((int)file.SourceCount)))
            .ToList();
        var tags = connection.Query<string>("""
            SELECT tag FROM local_item_tags
             WHERE item_id = @itemId
             ORDER BY tag COLLATE NOCASE;
            """, new { itemId = row.Id }).ToList();
        return new LocalAssetDto(
            row.Id,
            row.LibraryId,
            row.MediaKind,
            row.Title,
            row.FileName,
            row.MimeType,
            row.CapturedAt,
            row.CreatedAt,
            row.Width,
            row.Height,
            row.DurationSeconds,
            row.PageCount,
            row.DeviceMake,
            row.DeviceModel,
            row.Latitude,
            row.Longitude,
            row.LocationName,
            row.Favorite != 0,
            row.Hidden != 0,
            checked((int)row.SourceCount),
            files,
            tags,
            $"/view/{row.LibraryId:D}/items/{row.Id:D}/thumbnail",
            $"/view/{row.LibraryId:D}/items/{row.Id:D}/content");
    }

    private static LocalCollectionDto MapCollection(CollectionRow row) => new(
        row.Id,
        row.LibraryId,
        row.Name,
        row.Description,
        row.CollectionKind,
        checked((int)row.ItemCount),
        row.CoverItemId,
        row.CreatedAt);

    private static void ReplaceTags(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid itemId,
        IReadOnlyCollection<string> tags,
        DateTimeOffset now)
    {
        connection.Execute(
            "DELETE FROM local_item_tags WHERE item_id = @itemId;",
            new { itemId }, transaction);
        foreach (var tag in tags
                     .Where(tag => !string.IsNullOrWhiteSpace(tag))
                     .Select(tag => tag.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            connection.Execute("""
                INSERT INTO local_item_tags (item_id, tag, added_at)
                VALUES (@itemId, @tag, @now);
                """, new { itemId, tag, now }, transaction);
        }
    }

    private static void RebuildSearchDocument(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid itemId)
    {
        var row = connection.QuerySingle<SearchDocumentRow>("""
            SELECT li.title AS Title, li.primary_file_name AS FileName,
                   li.primary_mime_type AS MimeType, li.media_kind AS MediaKind,
                   li.captured_at AS CapturedAt,
                   trim(COALESCE(lm.device_make, '') || ' ' || COALESCE(lm.device_model, '')) AS Device,
                   lm.location_name AS Location, lm.document_text AS DocumentText,
                   (SELECT group_concat(tag, ' ') FROM local_item_tags WHERE item_id = li.id) AS Tags
              FROM local_items li
              LEFT JOIN local_item_metadata lm ON lm.item_id = li.id
             WHERE li.id = @itemId;
            """, new { itemId }, transaction);
        connection.Execute("""
            INSERT INTO local_item_search_keys (item_id)
            VALUES (@itemId)
            ON CONFLICT(item_id) DO NOTHING;
            """, new { itemId }, transaction);
        var searchRowId = connection.QuerySingle<long>(
            "SELECT rowid FROM local_item_search_keys WHERE item_id = @itemId;",
            new { itemId }, transaction);
        connection.Execute(
            "DELETE FROM local_item_search WHERE rowid = @searchRowId;",
            new { searchRowId }, transaction);
        connection.Execute("""
            INSERT INTO local_item_search
                (rowid, title, file_name, mime_type, media_kind, captured_at,
                 device, location, document_text, tags)
            VALUES
                (@searchRowId, @Title, @FileName, @MimeType, @MediaKind, @CapturedAt,
                 @Device, @Location, @DocumentText, @Tags);
            """, new
        {
            searchRowId,
            row.Title,
            row.FileName,
            row.MimeType,
            row.MediaKind,
            row.CapturedAt,
            row.Device,
            row.Location,
            row.DocumentText,
            row.Tags,
        }, transaction);
    }

    private static void ValidateQuery(LocalAssetQuery query)
    {
        if (query.LibraryId == Guid.Empty) throw new ArgumentException("Library ID is required.", nameof(query));
        if (query.Offset < 0) throw new ArgumentOutOfRangeException(nameof(query), "Offset cannot be negative.");
        if (query.Limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(query), "Limit must be between 1 and 500.");
        if (query.HiddenOnly && !query.IncludeHidden)
        {
            throw new ArgumentException("Hidden-only queries must include hidden items.", nameof(query));
        }
    }

    private static void ValidateRegistration(LocalAssetRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        if (registration.LibraryId == Guid.Empty) throw new ArgumentException("Library ID is required.", nameof(registration));
        NormalizeMediaKind(registration.MediaKind);
        if (registration.Files is null || registration.Files.Count == 0)
        {
            throw new ArgumentException("At least one physical file is required.", nameof(registration));
        }
        var primaryCount = registration.Files.Count(file =>
            string.Equals(file.Role, LocalAssetFileRoles.Primary, StringComparison.OrdinalIgnoreCase));
        if (primaryCount > 1)
        {
            throw new ArgumentException("A logical item can have only one primary file.", nameof(registration));
        }
        if (!registration.ExistingItemId.HasValue && primaryCount != 1)
        {
            throw new ArgumentException("A new logical item requires exactly one primary file.", nameof(registration));
        }
        if (registration.ExistingItemId.HasValue && registration.ExistingItemId.Value == Guid.Empty)
        {
            throw new ArgumentException("Existing item ID cannot be empty.", nameof(registration));
        }
        if (registration.Width < 0 || registration.Height < 0 || registration.DurationSeconds < 0 || registration.PageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registration), "Technical measurements cannot be negative.");
        }
        if (registration.Latitude is < -90 or > 90 || registration.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(registration), "GPS coordinates are outside their valid range.");
        }
        ValidateJson(registration.MetadataJson, nameof(registration.MetadataJson));
        foreach (var file in registration.Files)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(file.FilePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.FileName);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.MimeType);
            if (file.ByteSize < 0) throw new ArgumentOutOfRangeException(nameof(registration), "File size cannot be negative.");
            NormalizeHash(file.ContentHash);
            NormalizeRole(file.Role);
            if (string.Equals(file.Role, LocalAssetFileRoles.Derivative, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(file.DerivativeKind))
            {
                throw new ArgumentException("Derivative files require a derivative kind.", nameof(registration));
            }
        }
    }

    private static void ValidateAnnotation(LocalAssetAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(annotation.Source);
        if (annotation.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(annotation), "Confidence must be between zero and one.");
        }
        ValidateJson(annotation.ProvenanceJson, nameof(annotation.ProvenanceJson));
    }

    private static void ValidateJson(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try
        {
            using var _ = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Value must contain valid JSON.", parameterName, exception);
        }
    }

    private static string NormalizeHash(string hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var normalized = hash.Trim().ToLowerInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Content hash must be a 64-character SHA-256 hexadecimal value.", nameof(hash));
        }
        return normalized;
    }

    private static string NormalizeMediaKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var normalized = kind.Trim().ToLowerInvariant();
        if (!LocalAssetMediaKinds.All.Contains(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), $"Unsupported local media kind '{kind}'.");
        }
        return normalized;
    }

    private static string NormalizeRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var normalized = role.Trim().ToLowerInvariant();
        if (!LocalAssetFileRoles.All.Contains(normalized))
        {
            throw new ArgumentOutOfRangeException(nameof(role), $"Unsupported local file role '{role}'.");
        }
        return normalized;
    }

    private static string NormalizeCollectionKind(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var normalized = kind.Trim().ToLowerInvariant();
        if (normalized is not ("album" or "collection"))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Collection kind must be 'album' or 'collection'.");
        }
        return normalized;
    }

    private static string? BuildSearchExpression(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Replace("\"", "\"\"", StringComparison.Ordinal))
            .Where(term => term.Length != 0)
            .Take(20)
            .Select(term => $"\"{term}\"*")
            .ToArray();
        return terms.Length == 0 ? null : string.Join(" AND ", terms);
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
