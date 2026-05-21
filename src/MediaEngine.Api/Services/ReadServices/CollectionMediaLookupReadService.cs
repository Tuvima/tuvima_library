using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ICollectionMediaLookupReadService
{
    Task<List<CollectionMediaLookupDto>> LookupAsync(
        string? query,
        string? mediaTypes,
        IReadOnlySet<Guid> existingWorkIds,
        int? offset,
        int? limit,
        CancellationToken ct);

    Task<List<CollectionItemDto>> ResolveItemsAsync(
        Guid collectionId,
        IReadOnlyList<CollectionItem> items,
        CancellationToken ct);
}

public sealed class CollectionMediaLookupReadService(IDatabaseConnection db) : ICollectionMediaLookupReadService
{
    public async Task<List<CollectionMediaLookupDto>> LookupAsync(
        string? query,
        string? mediaTypes,
        IReadOnlySet<Guid> existingWorkIds,
        int? offset,
        int? limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit ?? 24, 1, 100);
        var skip = Math.Max(0, offset ?? 0);
        var normalizedQuery = (query ?? string.Empty).Trim();
        var searchLike = string.IsNullOrWhiteSpace(normalizedQuery) ? null : $"%{normalizedQuery}%";
        var requestedMediaTypes = (mediaTypes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<CollectionMediaLookupRow>(new CommandDefinition(
            $"""
            WITH RECURSIVE work_descendants(root_id, work_id, depth) AS (
                SELECT id, id, 0
                FROM works
                UNION ALL
                SELECT work_descendants.root_id, child.id, work_descendants.depth + 1
                FROM works child
                INNER JOIN work_descendants ON child.parent_work_id = work_descendants.work_id
            ),
            representative_assets AS (
                SELECT work_descendants.root_id AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_descendants
                INNER JOIN editions e ON e.work_id = work_descendants.work_id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_descendants.root_id
            )
            SELECT w.id AS WorkId,
                   w.media_type AS MediaType,
                   w.work_kind AS WorkKind,
                   w.ordinal AS Ordinal,
                   ra.AssetId,
                   COALESCE(NULLIF(episode_title.value, ''), NULLIF(title_work.value, ''), NULLIF(title_asset.value, ''), 'Untitled') AS Title,
                   COALESCE(NULLIF(author_work.value, ''), NULLIF(artist_work.value, ''), NULLIF(artist_root.value, ''), NULLIF(author_asset.value, ''), NULLIF(artist_asset.value, '')) AS Creator,
                   COALESCE(NULLIF(year_work.value, ''), NULLIF(year_asset.value, ''), NULLIF(year_root.value, '')) AS Year,
                   COALESCE(NULLIF(cover_work.value, ''), NULLIF(cover_asset.value, ''), NULLIF(cover_root.value, '')) AS ArtworkUrl,
                   COALESCE(NULLIF(show_root.value, ''), NULLIF(show_work.value, ''), NULLIF(title_root.value, '')) AS ShowName,
                   COALESCE(NULLIF(season_work.value, ''), NULLIF(season_asset.value, '')) AS SeasonNumber,
                   COALESCE(NULLIF(album_root.value, ''), NULLIF(album_work.value, ''), NULLIF(album_asset.value, '')) AS Album,
                   COALESCE(NULLIF(artist_root.value, ''), NULLIF(artist_work.value, ''), NULLIF(artist_asset.value, '')) AS Artist
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values title_root ON title_root.entity_id = COALESCE(gp.id, p.id, w.id) AND title_root.key = 'title'
            LEFT JOIN canonical_values episode_title ON episode_title.entity_id = w.id AND episode_title.key = 'episode_title'
            LEFT JOIN canonical_values author_work ON author_work.entity_id = w.id AND author_work.key = 'author'
            LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values artist_root ON artist_root.entity_id = COALESCE(gp.id, p.id, w.id) AND artist_root.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values year_work ON year_work.entity_id = w.id AND year_work.key IN ('year', 'release_year')
            LEFT JOIN canonical_values year_root ON year_root.entity_id = COALESCE(gp.id, p.id, w.id) AND year_root.key IN ('year', 'release_year')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values cover_root ON cover_root.entity_id = COALESCE(gp.id, p.id, w.id) AND cover_root.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values show_work ON show_work.entity_id = w.id AND show_work.key = 'show_name'
            LEFT JOIN canonical_values show_root ON show_root.entity_id = COALESCE(gp.id, p.id, w.id) AND show_root.key IN ('show_name', 'title')
            LEFT JOIN canonical_values season_work ON season_work.entity_id = w.id AND season_work.key = 'season_number'
            LEFT JOIN canonical_values album_work ON album_work.entity_id = w.id AND album_work.key = 'album'
            LEFT JOIN canonical_values album_root ON album_root.entity_id = COALESCE(gp.id, p.id, w.id) AND album_root.key = 'album'
            LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ra.AssetId AND title_asset.key = 'title'
            LEFT JOIN canonical_values author_asset ON author_asset.entity_id = ra.AssetId AND author_asset.key = 'author'
            LEFT JOIN canonical_values artist_asset ON artist_asset.entity_id = ra.AssetId AND artist_asset.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values year_asset ON year_asset.entity_id = ra.AssetId AND year_asset.key IN ('year', 'release_year')
            LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ra.AssetId AND cover_asset.key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still')
            LEFT JOIN canonical_values season_asset ON season_asset.entity_id = ra.AssetId AND season_asset.key = 'season_number'
            LEFT JOIN canonical_values album_asset ON album_asset.entity_id = ra.AssetId AND album_asset.key = 'album'
            WHERE w.work_kind != 'catalog'
              AND ra.AssetId IS NOT NULL
              AND {visibleWorkPredicate}
              AND (@mediaTypeCount = 0 OR w.media_type IN @mediaTypes)
              AND (
                    @searchLike IS NULL
                 OR title_work.value LIKE @searchLike
                 OR title_asset.value LIKE @searchLike
                 OR episode_title.value LIKE @searchLike
                 OR author_work.value LIKE @searchLike
                 OR artist_work.value LIKE @searchLike
                 OR artist_root.value LIKE @searchLike
                 OR album_root.value LIKE @searchLike
                 OR album_work.value LIKE @searchLike
                 OR show_root.value LIKE @searchLike
                 OR show_work.value LIKE @searchLike
              )
            ORDER BY Title COLLATE NOCASE, w.id
            LIMIT @limit OFFSET @offset;
            """,
            new
            {
                searchLike,
                mediaTypes = requestedMediaTypes,
                mediaTypeCount = requestedMediaTypes.Length,
                limit = take,
                offset = skip,
            },
            cancellationToken: ct))).ToList();

        return rows.Select(row => new CollectionMediaLookupDto
        {
            WorkId = row.WorkId,
            Title = row.Title,
            Subtitle = BuildLookupSubtitle(row),
            Creator = row.Creator,
            MediaType = row.MediaType,
            Year = row.Year,
            ArtworkUrl = !string.IsNullOrWhiteSpace(row.ArtworkUrl)
                ? row.ArtworkUrl
                : row.AssetId.HasValue ? $"/stream/{row.AssetId.Value}/cover" : null,
            ParentContext = BuildLookupParentContext(row),
            Route = BuildLookupRoute(row),
            AlreadyInCollection = existingWorkIds.Contains(row.WorkId),
        }).ToList();
    }

    public async Task<List<CollectionItemDto>> ResolveItemsAsync(
        Guid collectionId,
        IReadOnlyList<CollectionItem> items,
        CancellationToken ct)
    {
        var dtos = new List<CollectionItemDto>();
        if (items.Count == 0)
        {
            return dtos;
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleWorkIds = (await conn.QueryAsync<Guid>(
            $"""
            SELECT w.id
            FROM works w
            WHERE w.id IN @ids
              AND {visibleWorkPredicate}
            """,
            new { ids = items.Select(item => item.WorkId.ToString("D")).ToArray() }))
            .ToHashSet();

        foreach (var item in items)
        {
            if (!visibleWorkIds.Contains(item.WorkId))
            {
                continue;
            }

            string? title = null;
            string? creator = null;
            string? mediaType = null;
            string? cover = null;
            using var cmd = conn.CreateCommand();
            var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
            cmd.CommandText = $"""
                SELECT cv.key, cv.value
                FROM canonical_values cv
                WHERE cv.entity_id = @WorkId
                  AND cv.key IN ('title', 'episode_title', 'show_name', 'author', 'artist')
                UNION ALL
                SELECT 'title', smi.item_label
                FROM series_manifest_items smi
                WHERE smi.linked_work_id = @WorkId
                  AND smi.collection_id = @CollectionId
                UNION ALL
                SELECT 'media_type', w.media_type
                FROM works w WHERE w.id = @WorkId
                UNION ALL
                SELECT '_asset_id', MIN(ma.id)
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @WorkId
                  AND {visibleAssetPredicate}
                """;
            var workParam = cmd.CreateParameter();
            workParam.ParameterName = "@WorkId";
            workParam.Value = item.WorkId.ToString("D");
            cmd.Parameters.Add(workParam);
            var collectionParam = cmd.CreateParameter();
            collectionParam.ParameterName = "@CollectionId";
            collectionParam.Value = collectionId.ToString("D");
            cmd.Parameters.Add(collectionParam);

            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = reader.GetString(0);
                var value = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                switch (key)
                {
                    case "title":
                    case "episode_title":
                    case "show_name":
                        title ??= value;
                        break;
                    case "author":
                    case "artist":
                        creator ??= value;
                        break;
                    case "_asset_id":
                        cover = $"/stream/{value}/cover";
                        break;
                    case "media_type":
                        mediaType = value;
                        break;
                }
            }

            dtos.Add(new CollectionItemDto
            {
                Id = item.Id,
                WorkId = item.WorkId,
                Title = title ?? "Untitled",
                Creator = creator,
                MediaType = mediaType ?? "Unknown",
                CoverUrl = cover,
                SortOrder = item.SortOrder,
            });
        }

        return dtos;
    }

    private static string? BuildLookupSubtitle(CollectionMediaLookupRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Creator))
        {
            parts.Add(row.Creator);
        }

        if (!string.IsNullOrWhiteSpace(row.Year))
        {
            parts.Add(row.Year);
        }

        if (!string.IsNullOrWhiteSpace(row.MediaType))
        {
            parts.Add(row.MediaType);
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string? BuildLookupParentContext(CollectionMediaLookupRow row)
    {
        if (string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase))
        {
            return row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase) ? "Album"
                : row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase) ? "Series"
                : row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase) ? "Series"
                : row.MediaType.Contains("book", StringComparison.OrdinalIgnoreCase) ? "Series"
                : "Container";
        }

        if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.ShowName))
            {
                parts.Add(row.ShowName);
            }

            if (!string.IsNullOrWhiteSpace(row.SeasonNumber))
            {
                parts.Add($"Season {row.SeasonNumber}");
            }

            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        if (row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.Artist))
            {
                parts.Add(row.Artist);
            }

            if (!string.IsNullOrWhiteSpace(row.Album))
            {
                parts.Add(row.Album);
            }

            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        return null;
    }

    private static string BuildLookupRoute(CollectionMediaLookupRow row)
    {
        if (string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase))
        {
            if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/tvshow/{row.WorkId:D}?context=watch";
            }

            if (row.MediaType.Contains("Music", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/musicalbum/{row.WorkId:D}?context=listen";
            }

            if (row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/comicseries/{row.WorkId:D}?context=comics";
            }

            if (row.MediaType.Contains("book", StringComparison.OrdinalIgnoreCase))
            {
                return $"/details/bookseries/{row.WorkId:D}?context=read";
            }
        }

        if (row.MediaType.Contains("TV", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/tvepisode/{row.WorkId:D}?context=watch";
        }

        if (row.MediaType.Contains("movie", StringComparison.OrdinalIgnoreCase))
        {
            return $"/watch/movie/{row.WorkId:D}";
        }

        if (row.MediaType.Contains("music", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/musictrack/{row.WorkId:D}?context=listen";
        }

        if (row.MediaType.Contains("audio", StringComparison.OrdinalIgnoreCase))
        {
            return $"/listen/audiobook/{row.WorkId:D}";
        }

        if (row.MediaType.Contains("comic", StringComparison.OrdinalIgnoreCase))
        {
            return $"/details/comicissue/{row.WorkId:D}?context=comics";
        }

        return $"/book/{row.WorkId:D}";
    }

    private sealed record CollectionMediaLookupRow(
        Guid WorkId,
        string MediaType,
        string? WorkKind,
        int? Ordinal,
        Guid? AssetId,
        string Title,
        string? Creator,
        string? Year,
        string? ArtworkUrl,
        string? ShowName,
        string? SeasonNumber,
        string? Album,
        string? Artist);
}
