using Dapper;
using MediaEngine.Contracts.Photos;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>Persistence boundary for local photo assets and albums.</summary>
public sealed class PhotoLibraryRepository(IDatabaseConnection database)
{
    private sealed class PhotoRow
    {
        public Guid Id { get; init; }
        public string FileName { get; init; } = string.Empty;
        public DateTimeOffset CapturedAt { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string MimeType { get; init; } = string.Empty;
        public double? Latitude { get; init; }
        public double? Longitude { get; init; }
        public string? CameraMake { get; init; }
        public string? CameraModel { get; init; }
        public long Favorite { get; init; }
        public long Hidden { get; init; }
        public long DuplicateCount { get; init; }
        public long TotalCount { get; init; }
    }

    private sealed class PhotoPathRow
    {
        public string FilePath { get; init; } = string.Empty;
        public string MimeType { get; init; } = string.Empty;
    }

    private sealed class AlbumRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public long ItemCount { get; init; }
        public Guid? CoverId { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
    }

    public (IReadOnlyList<PhotoAssetDto> Items, int Total) Query(
        int offset, int limit, string? search, bool favorites, bool includeHidden, Guid? albumId,
        bool hiddenOnly = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var rows = connection.Query<PhotoRow>("""
            SELECT p.id AS Id,
                   p.file_name AS FileName,
                   p.captured_at AS CapturedAt,
                   p.width AS Width,
                   p.height AS Height,
                   p.mime_type AS MimeType,
                   pm.latitude AS Latitude,
                   pm.longitude AS Longitude,
                   pm.camera_make AS CameraMake,
                   pm.camera_model AS CameraModel,
                   p.favorite AS Favorite,
                   p.hidden AS Hidden,
                   COUNT(DISTINCT ps.id) AS DuplicateCount,
                   COUNT(*) OVER() AS TotalCount
            FROM photo_assets p
            JOIN photo_sources ps ON ps.photo_asset_id = p.id
            LEFT JOIN photo_metadata pm ON pm.photo_asset_id = p.id
            LEFT JOIN photo_album_items pai ON pai.photo_asset_id = p.id AND pai.album_id = @albumId
            WHERE ((@hiddenOnly = 1 AND p.hidden = 1)
                   OR (@hiddenOnly = 0 AND (@includeHidden = 1 OR p.hidden = 0)))
              AND (@favorites = 0 OR p.favorite = 1)
              AND (@search IS NULL OR p.file_name LIKE '%' || @search || '%' COLLATE NOCASE)
              AND (@albumId IS NULL OR pai.album_id IS NOT NULL)
            GROUP BY p.id
            ORDER BY p.captured_at DESC, p.id
            LIMIT @limit OFFSET @offset;
            """, new
        {
            offset,
            limit,
            search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            favorites = favorites ? 1 : 0,
            includeHidden = includeHidden ? 1 : 0,
            hiddenOnly = hiddenOnly ? 1 : 0,
            albumId,
        }).ToList();

        var total = rows.Count == 0 ? 0 : checked((int)rows[0].TotalCount);
        return (rows.Select(ToDto).ToList(), total);
    }

    public PhotoAssetDto? Find(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QuerySingleOrDefault<PhotoRow>("""
            SELECT p.id AS Id, p.file_name AS FileName, p.captured_at AS CapturedAt,
                   p.width AS Width, p.height AS Height, p.mime_type AS MimeType,
                   pm.latitude AS Latitude, pm.longitude AS Longitude,
                   pm.camera_make AS CameraMake, pm.camera_model AS CameraModel,
                   p.favorite AS Favorite, p.hidden AS Hidden,
                   COUNT(ps.id) AS DuplicateCount, 1 AS TotalCount
            FROM photo_assets p
            JOIN photo_sources ps ON ps.photo_asset_id = p.id
            LEFT JOIN photo_metadata pm ON pm.photo_asset_id = p.id
            WHERE p.id = @id
            GROUP BY p.id;
            """, new { id });
        return row is null ? null : ToDto(row);
    }

    public (string FilePath, string MimeType)? ResolveContent(Guid id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        var row = connection.QueryFirstOrDefault<PhotoPathRow>("""
            SELECT ps.file_path AS FilePath, p.mime_type AS MimeType
            FROM photo_assets p
            JOIN photo_sources ps ON ps.photo_asset_id = p.id
            WHERE p.id = @id
            ORDER BY ps.indexed_at DESC
            LIMIT 1;
            """, new { id });
        return row is null ? null : (row.FilePath, row.MimeType);
    }

    public Task<(bool PhotoAdded, bool SourceAdded)> UpsertAsync(
        Guid libraryId, string path, string hash, string fileName, DateTimeOffset capturedAt,
        int? width, int? height, string mimeType, long fileSize, DateTimeOffset modifiedAt,
        double? latitude = null, double? longitude = null,
        string? cameraMake = null, string? cameraModel = null,
        CancellationToken ct = default)
    {
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var existingId = connection.QuerySingleOrDefault<Guid?>(
                "SELECT id FROM photo_assets WHERE content_hash = @hash;",
                new { hash }, transaction);
            var photoId = existingId ?? Guid.NewGuid();
            var photoAdded = !existingId.HasValue;
            if (photoAdded)
            {
                connection.Execute("""
                    INSERT INTO photo_assets
                        (id, content_hash, file_name, captured_at, width, height, mime_type,
                         favorite, hidden, created_at)
                    VALUES (@photoId, @hash, @fileName, @capturedAt, @width, @height, @mimeType,
                            0, 0, @now);
                    """, new
                {
                    photoId,
                    hash,
                    fileName,
                    capturedAt,
                    width,
                    height,
                    mimeType,
                    now,
                }, transaction);
            }

            connection.Execute("""
                INSERT INTO photo_metadata
                    (photo_asset_id, latitude, longitude, camera_make, camera_model, updated_at)
                VALUES (@photoId, @latitude, @longitude, @cameraMake, @cameraModel, @now)
                ON CONFLICT(photo_asset_id) DO UPDATE SET
                    latitude = COALESCE(excluded.latitude, photo_metadata.latitude),
                    longitude = COALESCE(excluded.longitude, photo_metadata.longitude),
                    camera_make = COALESCE(excluded.camera_make, photo_metadata.camera_make),
                    camera_model = COALESCE(excluded.camera_model, photo_metadata.camera_model),
                    updated_at = excluded.updated_at;
                """, new
            {
                photoId,
                latitude,
                longitude,
                cameraMake,
                cameraModel,
                now,
            }, transaction);

            var sourceAdded = connection.Execute("""
                INSERT OR IGNORE INTO photo_sources
                    (id, photo_asset_id, library_id, file_path, file_size, modified_at, indexed_at)
                VALUES (@id, @photoId, @libraryId, @path, @fileSize, @modifiedAt, @now);
                """, new
            {
                id = Guid.NewGuid(),
                photoId,
                libraryId = libraryId.ToString("D"),
                path,
                fileSize,
                modifiedAt,
                now,
            }, transaction) > 0;
            return (photoAdded, sourceAdded);
        }, ct);
    }

    public Task<bool> SetFlagAsync(Guid id, string flag, bool value, CancellationToken ct = default)
    {
        if (flag is not ("favorite" or "hidden"))
        {
            throw new ArgumentOutOfRangeException(nameof(flag));
        }

        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            return connection.Execute(
                $"UPDATE photo_assets SET {flag} = @value WHERE id = @id;",
                new { id, value = value ? 1 : 0 }, transaction) > 0;
        }, ct);
    }

    public IReadOnlyList<PhotoAlbumDto> GetAlbums(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var connection = database.CreateConnection();
        return connection.Query<AlbumRow>("""
            SELECT a.id AS Id, a.name AS Name, a.description AS Description,
                   COUNT(i.photo_asset_id) AS ItemCount,
                   (SELECT i2.photo_asset_id FROM photo_album_items i2
                    WHERE i2.album_id = a.id ORDER BY i2.position, i2.added_at LIMIT 1) AS CoverId,
                   a.created_at AS CreatedAt
            FROM photo_albums a
            LEFT JOIN photo_album_items i ON i.album_id = a.id
            GROUP BY a.id
            ORDER BY a.modified_at DESC;
            """).Select(row => new PhotoAlbumDto(
                row.Id, row.Name, row.Description, checked((int)row.ItemCount),
                row.CoverId.HasValue ? $"/photos/{row.CoverId:D}/thumbnail" : null,
                row.CreatedAt)).ToList();
    }

    public Task<PhotoAlbumDto> CreateAlbumAsync(string name, string? description, CancellationToken ct = default)
    {
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            connection.Execute("""
                INSERT INTO photo_albums (id, name, description, created_at, modified_at)
                VALUES (@id, @name, @description, @now, @now);
                """, new { id, name = name.Trim(), description, now }, transaction);
            return new PhotoAlbumDto(id, name.Trim(), description, 0, null, now);
        }, ct);
    }

    public Task<int> AddToAlbumAsync(Guid albumId, IReadOnlyList<Guid> photoIds, CancellationToken ct = default)
    {
        return database.ExecuteWriteAsync((connection, transaction, token) =>
        {
            token.ThrowIfCancellationRequested();
            var position = connection.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(position), -1) + 1 FROM photo_album_items WHERE album_id = @albumId;",
                new { albumId }, transaction);
            var added = 0;
            foreach (var photoId in photoIds.Distinct())
            {
                added += connection.Execute("""
                    INSERT OR IGNORE INTO photo_album_items (album_id, photo_asset_id, position, added_at)
                    SELECT @albumId, @photoId, @position, @now
                    WHERE EXISTS (SELECT 1 FROM photo_albums WHERE id = @albumId)
                      AND EXISTS (SELECT 1 FROM photo_assets WHERE id = @photoId);
                    """, new { albumId, photoId, position, now = DateTimeOffset.UtcNow }, transaction);
                position++;
            }
            connection.Execute(
                "UPDATE photo_albums SET modified_at = @now WHERE id = @albumId;",
                new { albumId, now = DateTimeOffset.UtcNow }, transaction);
            return added;
        }, ct);
    }

    private static PhotoAssetDto ToDto(PhotoRow row) => new(
        row.Id, row.FileName, row.CapturedAt, row.Width, row.Height, row.MimeType,
        row.Latitude, row.Longitude, row.CameraMake, row.CameraModel,
        row.Favorite != 0, row.Hidden != 0, checked((int)row.DuplicateCount),
        $"/photos/{row.Id:D}/thumbnail", $"/photos/{row.Id:D}/content");
}
