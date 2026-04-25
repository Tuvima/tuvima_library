using Dapper;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayWorkProjectionReader
{
    private readonly IDatabaseConnection _db;

    public DisplayWorkProjectionReader(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DisplayWorkRow>> LoadAsync(CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var visibleWorkPredicate = HomeVisibilitySql.VisibleWorkPredicate("w.id", "w.curator_state", "w.is_catalog_only");
        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var sql = $"""
            WITH ranked_assets AS (
                SELECT
                    w.id AS WorkId,
                    w.collection_id AS CollectionId,
                    w.media_type AS MediaType,
                    w.work_kind AS WorkKind,
                    COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                    ma.id AS AssetId,
                    MIN(mc.claimed_at) OVER (PARTITION BY w.id) AS CreatedAt,
                    ROW_NUMBER() OVER (
                        PARTITION BY w.id
                        ORDER BY CASE WHEN mc.claimed_at IS NULL THEN 1 ELSE 0 END, mc.claimed_at ASC, ma.id
                    ) AS AssetRank
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN metadata_claims mc ON mc.entity_id = ma.id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.work_kind != 'parent'
                  AND {visibleWorkPredicate}
                  AND {visibleAssetPredicate}
            )
            SELECT
                WorkId,
                CollectionId,
                MediaType,
                WorkKind,
                RootWorkId,
                AssetId,
                COALESCE(CreatedAt, CURRENT_TIMESTAMP) AS CreatedAt,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'title' LIMIT 1),
                         'Untitled') AS Title,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('author', 'creator') LIMIT 1) AS Author,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'artist' LIMIT 1) AS Artist,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'album' LIMIT 1) AS Album,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('release_year', 'year') LIMIT 1) AS Year,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'genre' LIMIT 1) AS Genre,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'series' LIMIT 1) AS Series,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'series_position' LIMIT 1) AS SeriesPosition,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'narrator' LIMIT 1) AS Narrator,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'director' LIMIT 1) AS Director,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'network' LIMIT 1) AS Network,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'show_name' LIMIT 1) AS ShowName,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'season_number' LIMIT 1) AS SeasonNumber,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'episode_number' LIMIT 1) AS EpisodeNumber,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'track_number' LIMIT 1) AS TrackNumber,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover') LIMIT 1) AS CoverUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('square_url', 'square') LIMIT 1) AS SquareUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url', 'banner') LIMIT 1) AS BannerUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url', 'background') LIMIT 1) AS BackgroundUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('logo_url', 'logo') LIMIT 1) AS LogoUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_state' LIMIT 1)) AS CoverState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_state' LIMIT 1)) AS SquareState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'banner_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_state' LIMIT 1)) AS BannerState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_state' LIMIT 1)) AS BackgroundState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'logo_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'logo_state' LIMIT 1)) AS LogoState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_width_px' LIMIT 1) AS CoverWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_height_px' LIMIT 1) AS CoverHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_width_px' LIMIT 1) AS SquareWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_height_px' LIMIT 1) AS SquareHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_width_px' LIMIT 1) AS BannerWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_height_px' LIMIT 1) AS BannerHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_width_px' LIMIT 1) AS BackgroundWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_height_px' LIMIT 1) AS BackgroundHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'artwork_accent_hex' LIMIT 1) AS AccentColor
            FROM ranked_assets
            WHERE AssetRank = 1
            ORDER BY CreatedAt DESC;
            """;

        var rows = (await conn.QueryAsync<DisplayWorkRow>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            row.CoverUrl = DisplayArtworkUrlResolver.Resolve(row.CoverUrl, row.AssetId, "cover", row.CoverState);
            row.SquareUrl = DisplayArtworkUrlResolver.Resolve(row.SquareUrl, row.AssetId, "square", row.SquareState);
            row.BannerUrl = DisplayArtworkUrlResolver.Resolve(row.BannerUrl, row.AssetId, "banner", row.BannerState);
            row.BackgroundUrl = DisplayArtworkUrlResolver.Resolve(row.BackgroundUrl, row.AssetId, "background", row.BackgroundState);
            row.LogoUrl = DisplayArtworkUrlResolver.Resolve(row.LogoUrl, row.AssetId, "logo", row.LogoState);
        }

        return rows;
    }
}
