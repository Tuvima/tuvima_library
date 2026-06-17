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
                COALESCE(
                    NULLIF(TRIM((SELECT wikidata_qid FROM works WHERE id = WorkId LIMIT 1)), ''),
                    NULLIF(TRIM((SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'wikidata_qid' LIMIT 1)), ''),
                    NULLIF(TRIM((SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'wikidata_qid' LIMIT 1)), ''),
                    NULLIF(TRIM((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'wikidata_qid' LIMIT 1)), '')
                ) AS IdentityQid,
                COALESCE(CreatedAt, CURRENT_TIMESTAMP) AS CreatedAt,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'issue_title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'issue_title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'episode_title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'episode_title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'title' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'title' LIMIT 1),
                         'Untitled') AS Title,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('author', 'creator') LIMIT 1) AS Author,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'artist' LIMIT 1) AS Artist,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'album' LIMIT 1) AS Album,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('release_year', 'year') LIMIT 1) AS Year,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'genre' LIMIT 1) AS Genre,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'series' LIMIT 1) AS Series,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'issue_number' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'issue_number' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'series_position' LIMIT 1)
                ) AS SeriesPosition,
                (SELECT display_name FROM collections WHERE id = CollectionId LIMIT 1) AS CollectionTitle,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'narrator' LIMIT 1) AS Narrator,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'director' LIMIT 1) AS Director,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('network', 'studio', 'broadcaster', 'streaming_service', 'platform') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('network', 'studio', 'broadcaster', 'streaming_service', 'platform') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('network', 'studio', 'broadcaster', 'streaming_service', 'platform') LIMIT 1)
                ) AS Network,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('source_service', 'source_platform') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('source_service', 'source_platform') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('source_service', 'source_platform') LIMIT 1)
                ) AS Source,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('quality', 'video_quality', 'resolution', 'video_resolution', 'video_resolution_label') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('quality', 'video_quality', 'resolution', 'video_resolution', 'video_resolution_label') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('quality', 'video_quality', 'resolution', 'video_resolution', 'video_resolution_label') LIMIT 1)
                ) AS Quality,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'show_name' LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'title' LIMIT 1)
                ) AS ShowName,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'season_number' LIMIT 1) AS SeasonNumber,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'episode_number' LIMIT 1) AS EpisodeNumber,
                (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'track_number' LIMIT 1) AS TrackNumber,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1)
                ) AS CoverUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_url_s', 'poster_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_url_s', 'poster_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_s', 'poster_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1)) AS CoverSmallUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_url_m', 'poster_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_url_m', 'poster_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_m', 'poster_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1)) AS CoverMediumUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_url_l', 'poster_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_url_l', 'poster_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_l', 'poster_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1)) AS CoverLargeUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('square_url', 'square') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('square_url', 'square') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('square_url', 'square') LIMIT 1)
                ) AS SquareUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_url_s' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_url_s' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_s' LIMIT 1)) AS SquareSmallUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_url_m' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_url_m' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_m' LIMIT 1)) AS SquareMediumUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_url_l' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_url_l' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_l' LIMIT 1)) AS SquareLargeUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_url', 'banner', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_url', 'banner', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url', 'banner', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1)
                ) AS BannerUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1)) AS BannerSmallUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1)) AS BannerMediumUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1)) AS BannerLargeUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_url', 'background', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_url', 'background', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url', 'background', 'episode_still_url', 'episode_still', 'still_url', 'still') LIMIT 1)
                ) AS BackgroundUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_s', 'episode_still_url_s', 'still_url_s') LIMIT 1)) AS BackgroundSmallUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_m', 'episode_still_url_m', 'still_url_m') LIMIT 1)) AS BackgroundMediumUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_l', 'episode_still_url_l', 'still_url_l') LIMIT 1)) AS BackgroundLargeUrl,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('logo_url', 'logo') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('logo_url', 'logo') LIMIT 1),
                    (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('logo_url', 'logo') LIMIT 1)
                ) AS LogoUrl,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'cover_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_state' LIMIT 1)) AS CoverState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_state' LIMIT 1)) AS SquareState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'banner_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'banner_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_state' LIMIT 1)) AS BannerState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'background_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_state' LIMIT 1)) AS BackgroundState,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'logo_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'logo_state' LIMIT 1),
                         (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'logo_state' LIMIT 1)) AS LogoState,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1) AS CollectionCoverUrl,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key IN ('square_url', 'square') LIMIT 1) AS CollectionSquareUrl,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key IN ('banner_url', 'banner') LIMIT 1) AS CollectionBannerUrl,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key IN ('background_url', 'background', 'hero_url', 'hero') LIMIT 1) AS CollectionBackgroundUrl,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key IN ('logo_url', 'logo') LIMIT 1) AS CollectionLogoUrl,
                (SELECT value FROM canonical_values WHERE entity_id = CollectionId AND key = 'artwork_accent_hex' LIMIT 1) AS CollectionAccentColor,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1) AS RootCoverUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_s', 'poster_url_s') LIMIT 1) AS RootCoverSmallUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_m', 'poster_url_m') LIMIT 1) AS RootCoverMediumUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_url_l', 'poster_url_l') LIMIT 1) AS RootCoverLargeUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('square_url', 'square') LIMIT 1) AS RootSquareUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_s' LIMIT 1) AS RootSquareSmallUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_m' LIMIT 1) AS RootSquareMediumUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_url_l' LIMIT 1) AS RootSquareLargeUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_url', 'banner') LIMIT 1) AS RootBannerUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_url_s' LIMIT 1) AS RootBannerSmallUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_url_m' LIMIT 1) AS RootBannerMediumUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_url_l' LIMIT 1) AS RootBannerLargeUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url', 'background', 'hero_url', 'hero') LIMIT 1) AS RootBackgroundUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_s', 'hero_url_s') LIMIT 1) AS RootBackgroundSmallUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_m', 'hero_url_m') LIMIT 1) AS RootBackgroundMediumUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_url_l', 'hero_url_l') LIMIT 1) AS RootBackgroundLargeUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('logo_url', 'logo') LIMIT 1) AS RootLogoUrl,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_state' LIMIT 1) AS RootCoverState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_state' LIMIT 1) AS RootSquareState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_state' LIMIT 1) AS RootBannerState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_state' LIMIT 1) AS RootBackgroundState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'logo_state' LIMIT 1) AS RootLogoState,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_width_px' LIMIT 1) AS RootCoverWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'cover_height_px' LIMIT 1) AS RootCoverHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_width_px' LIMIT 1) AS RootSquareWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_height_px' LIMIT 1) AS RootSquareHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_width_px' LIMIT 1) AS RootBannerWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'banner_height_px' LIMIT 1) AS RootBannerHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_width_px' LIMIT 1) AS RootBackgroundWidthPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'background_height_px' LIMIT 1) AS RootBackgroundHeightPx,
                (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'artwork_accent_hex' LIMIT 1) AS RootAccentColor,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_width_px', 'episode_still_width_px') LIMIT 1)) AS CoverWidthPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('cover_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('cover_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('cover_height_px', 'episode_still_height_px') LIMIT 1)) AS CoverHeightPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_width_px' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_width_px' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_width_px' LIMIT 1)) AS SquareWidthPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key = 'square_height_px' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key = 'square_height_px' LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key = 'square_height_px' LIMIT 1)) AS SquareHeightPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_width_px', 'episode_still_width_px') LIMIT 1)) AS BannerWidthPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('banner_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('banner_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('banner_height_px', 'episode_still_height_px') LIMIT 1)) AS BannerHeightPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_width_px', 'episode_still_width_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_width_px', 'episode_still_width_px') LIMIT 1)) AS BackgroundWidthPx,
                COALESCE((SELECT value FROM canonical_values WHERE entity_id = AssetId AND key IN ('background_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = WorkId AND key IN ('background_height_px', 'episode_still_height_px') LIMIT 1), (SELECT value FROM canonical_values WHERE entity_id = RootWorkId AND key IN ('background_height_px', 'episode_still_height_px') LIMIT 1)) AS BackgroundHeightPx,
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
            row.RootCoverUrl = DisplayArtworkUrlResolver.Resolve(row.RootCoverUrl, row.AssetId, "cover", row.RootCoverState);
            row.RootSquareUrl = DisplayArtworkUrlResolver.Resolve(row.RootSquareUrl, row.AssetId, "square", row.RootSquareState);
            row.RootBannerUrl = DisplayArtworkUrlResolver.Resolve(row.RootBannerUrl, row.AssetId, "banner", row.RootBannerState);
            row.RootBackgroundUrl = DisplayArtworkUrlResolver.Resolve(row.RootBackgroundUrl, row.AssetId, "background", row.RootBackgroundState);
            row.RootLogoUrl = DisplayArtworkUrlResolver.Resolve(row.RootLogoUrl, row.AssetId, "logo", row.RootLogoState);
        }

        return rows;
    }
}
