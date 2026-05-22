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
            SELECT CASE
                       WHEN w.work_kind = 'child' THEN COALESCE(gp.id, p.id, w.id)
                       WHEN w.work_kind = 'parent' AND p.id IS NOT NULL THEN COALESCE(gp.id, p.id, w.id)
                       ELSE w.id
                   END AS WorkId,
                   w.media_type AS MediaType,
                   CASE
                       WHEN COALESCE(gp.id, p.id, w.id) != w.id THEN 'parent'
                       ELSE w.work_kind
                   END AS WorkKind,
                   w.ordinal AS Ordinal,
                   ra.AssetId,
                   COALESCE(NULLIF(show_root.value, ''), NULLIF(title_root.value, ''), NULLIF(episode_title.value, ''), NULLIF(title_work.value, ''), NULLIF(title_asset.value, ''), 'Untitled') AS Title,
                   COALESCE(NULLIF(author_work.value, ''), NULLIF(artist_root.value, ''), NULLIF(artist_work.value, ''), NULLIF(author_asset.value, ''), NULLIF(artist_asset.value, '')) AS Creator,
                   COALESCE(NULLIF(year_work.value, ''), NULLIF(year_asset.value, ''), NULLIF(year_root.value, '')) AS Year,
                   COALESCE(NULLIF(cover_root.value, ''), NULLIF(cover_work.value, ''), NULLIF(cover_asset.value, '')) AS ArtworkUrl,
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

        return rows
            .GroupBy(row => row.WorkId)
            .Select(group => group.First())
            .Select(row => new CollectionMediaLookupDto
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
        if (items.Count == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var rows = (await conn.QueryAsync<ResolvedCollectionItemRow>(new CommandDefinition(
            $"""
            WITH requested(ItemId, SourceWorkId, SortOrder) AS (
                SELECT value ->> 'id',
                       value ->> 'work_id',
                       CAST(value ->> 'sort_order' AS INTEGER)
                FROM json_each(@ItemsJson)
            ),
            display_work AS (
                SELECT requested.ItemId,
                       requested.SortOrder,
                       CASE
                           WHEN w.work_kind = 'child' THEN COALESCE(gp.id, p.id, w.id)
                           WHEN w.work_kind = 'parent' AND p.id IS NOT NULL THEN COALESCE(gp.id, p.id, w.id)
                           ELSE w.id
                       END AS WorkId
                FROM requested
                INNER JOIN works w ON w.id = requested.SourceWorkId
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
            ),
            work_tree(RootWorkId, WorkId) AS (
                SELECT display_work.WorkId,
                       display_work.WorkId
                FROM display_work
                UNION ALL
                SELECT work_tree.RootWorkId,
                       child.id
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.WorkId
            ),
            representative_assets AS (
                SELECT work_tree.RootWorkId AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM work_tree
                INNER JOIN editions e ON e.work_id = work_tree.WorkId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE {visibleAssetPredicate}
                GROUP BY work_tree.RootWorkId
            )
            SELECT display_work.ItemId,
                   display_work.WorkId,
                   COALESCE(NULLIF(title_work.value, ''), NULLIF(show_name.value, ''), NULLIF(series_item.item_label, ''), 'Untitled') AS Title,
                   COALESCE(NULLIF(author_work.value, ''), NULLIF(artist_work.value, '')) AS Creator,
                   w.media_type AS MediaType,
                   COALESCE(NULLIF(cover_work.value, ''), CASE WHEN ra.AssetId IS NOT NULL THEN '/stream/' || ra.AssetId || '/cover' END) AS CoverUrl,
                   display_work.SortOrder
            FROM display_work
            INNER JOIN works w ON w.id = display_work.WorkId
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
            LEFT JOIN canonical_values show_name ON show_name.entity_id = w.id AND show_name.key = 'show_name'
            LEFT JOIN canonical_values author_work ON author_work.entity_id = w.id AND author_work.key = 'author'
            LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key IN ('artist', 'album_artist')
            LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key IN ('cover_url', 'cover', 'poster_url', 'poster')
            LEFT JOIN series_manifest_items series_item ON series_item.linked_work_id = w.id AND series_item.collection_id = @CollectionId
            WHERE ({visibleWorkPredicate} OR ra.AssetId IS NOT NULL)
            ORDER BY display_work.SortOrder, Title COLLATE NOCASE, w.id
            """,
            new
            {
                CollectionId = collectionId.ToString("D"),
                ItemsJson = System.Text.Json.JsonSerializer.Serialize(items.Select(item => new
                {
                    id = item.Id.ToString("D"),
                    work_id = item.WorkId.ToString("D"),
                    sort_order = item.SortOrder,
                })),
            },
            cancellationToken: ct))).ToList();

        return rows
            .GroupBy(row => row.WorkId)
            .Select(group => group.OrderBy(row => row.SortOrder).First())
            .Select(row => new CollectionItemDto
        {
            Id = row.ItemId,
            WorkId = row.WorkId,
            Title = row.Title,
            Creator = row.Creator,
            MediaType = row.MediaType,
            CoverUrl = row.CoverUrl,
            SortOrder = row.SortOrder,
        }).ToList();
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

    private sealed record ResolvedCollectionItemRow(
        Guid ItemId,
        Guid WorkId,
        string Title,
        string? Creator,
        string MediaType,
        string? CoverUrl,
        int SortOrder);
}
