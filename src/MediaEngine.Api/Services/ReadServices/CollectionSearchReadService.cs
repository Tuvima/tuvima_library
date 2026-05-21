using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ICollectionSearchReadService
{
    Task<List<SearchResultDto>> SearchAsync(string? query, CancellationToken ct);
}

public sealed class CollectionSearchReadService(IDatabaseConnection db) : ICollectionSearchReadService
{
    public async Task<List<SearchResultDto>> SearchAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return [];
        }

        var trimmed = query.Trim();
        var like = $"%{trimmed}%";
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");

        using var conn = db.CreateConnection();
        var rows = (await conn.QueryAsync<CollectionSearchRow>(new CommandDefinition($"""
            WITH matched AS (
                SELECT
                    w.id AS WorkId,
                    w.collection_id AS CollectionId,
                    w.media_type AS MediaType,
                    COALESCE(
                        title_asset.value,
                        episode_title.value,
                        title_work.value,
                        original_title.value,
                        'Work ' || substr(w.id, 1, 8)
                    ) AS Title,
                    COALESCE(
                        author_asset.value,
                        artist_asset.value,
                        director_asset.value,
                        author_work.value,
                        artist_work.value,
                        director_work.value
                    ) AS Author,
                    COALESCE(
                        c.display_name,
                        collection_title.value,
                        substr(COALESCE(w.collection_id, w.id), 1, 8)
                    ) AS CollectionDisplayName,
                    COALESCE(
                        cover_asset.value,
                        cover_url_asset.value,
                        cover_work.value,
                        cover_url_work.value
                    ) AS CoverUrl,
                    ROW_NUMBER() OVER (
                        PARTITION BY w.id
                        ORDER BY
                            CASE WHEN title_asset.value IS NULL AND title_work.value IS NULL THEN 1 ELSE 0 END,
                            ma.id
                    ) AS RowNumber
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN collections c ON c.id = w.collection_id
                LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ma.id AND title_asset.key = 'title'
                LEFT JOIN canonical_values episode_title ON episode_title.entity_id = ma.id AND episode_title.key = 'episode_title'
                LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
                LEFT JOIN canonical_values original_title ON original_title.entity_id = w.id AND original_title.key = 'original_title'
                LEFT JOIN canonical_values author_asset ON author_asset.entity_id = ma.id AND author_asset.key = 'author'
                LEFT JOIN canonical_values artist_asset ON artist_asset.entity_id = ma.id AND artist_asset.key = 'artist'
                LEFT JOIN canonical_values director_asset ON director_asset.entity_id = ma.id AND director_asset.key = 'director'
                LEFT JOIN canonical_values author_work ON author_work.entity_id = w.id AND author_work.key = 'author'
                LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key = 'artist'
                LEFT JOIN canonical_values director_work ON director_work.entity_id = w.id AND director_work.key = 'director'
                LEFT JOIN canonical_values collection_title ON collection_title.entity_id = w.collection_id AND collection_title.key = 'title'
                LEFT JOIN canonical_values cover_asset ON cover_asset.entity_id = ma.id AND cover_asset.key = 'cover'
                LEFT JOIN canonical_values cover_url_asset ON cover_url_asset.entity_id = ma.id AND cover_url_asset.key = 'cover_url'
                LEFT JOIN canonical_values cover_work ON cover_work.entity_id = w.id AND cover_work.key = 'cover'
                LEFT JOIN canonical_values cover_url_work ON cover_url_work.entity_id = w.id AND cover_url_work.key = 'cover_url'
                WHERE w.work_kind != 'parent'
                  AND {visibleWorkPredicate}
                  AND {visibleAssetPredicate}
                  AND (
                      c.display_name LIKE @like COLLATE NOCASE
                      OR EXISTS (
                          SELECT 1
                          FROM canonical_values cv
                          WHERE cv.entity_id IN (ma.id, w.id, w.collection_id)
                            AND cv.value LIKE @like COLLATE NOCASE
                      )
                  )
            )
            SELECT WorkId,
                   CollectionId,
                   MediaType,
                   Title,
                   Author,
                   CollectionDisplayName,
                   CoverUrl
            FROM matched
            WHERE RowNumber = 1
            ORDER BY Title COLLATE NOCASE
            LIMIT 20;
            """, new { like }, cancellationToken: ct))).ToList();

        return rows.Select(row => new SearchResultDto
        {
            WorkId = row.WorkId,
            CollectionId = row.CollectionId,
            Title = row.Title,
            Author = row.Author,
            MediaType = row.MediaType ?? string.Empty,
            CollectionDisplayName = row.CollectionDisplayName ?? string.Empty,
            CoverUrl = row.CoverUrl,
        }).ToList();
    }

    private sealed record CollectionSearchRow(
        Guid WorkId,
        Guid? CollectionId,
        string? MediaType,
        string Title,
        string? Author,
        string? CollectionDisplayName,
        string? CoverUrl);
}
