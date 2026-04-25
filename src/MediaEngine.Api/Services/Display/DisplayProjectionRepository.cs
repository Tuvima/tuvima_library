using Dapper;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public interface IDisplayProjectionRepository
{
    Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct);
    Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct);
    Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct);
}

public sealed class DisplayProjectionRepository : IDisplayProjectionRepository
{
    private readonly IDatabaseConnection _db;

    public DisplayProjectionRepository(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct)
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
            row.CoverUrl = ResolveArtworkUrl(row.CoverUrl, row.AssetId, "cover", row.CoverState);
            row.SquareUrl = ResolveArtworkUrl(row.SquareUrl, row.AssetId, "square", row.SquareState);
            row.BannerUrl = ResolveArtworkUrl(row.BannerUrl, row.AssetId, "banner", row.BannerState);
            row.BackgroundUrl = ResolveArtworkUrl(row.BackgroundUrl, row.AssetId, "background", row.BackgroundState);
            row.LogoUrl = ResolveArtworkUrl(row.LogoUrl, row.AssetId, "logo", row.LogoState);
        }

        return rows;
    }

    public async Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var sql = """
            SELECT
                us.asset_id AS AssetId,
                w.id AS WorkId,
                w.collection_id AS CollectionId,
                w.media_type AS MediaType,
                us.progress_pct AS ProgressPct,
                us.last_accessed AS LastAccessed,
                COALESCE(cv_title_a.value, cv_title_w.value, 'Untitled') AS Title,
                cv_author_w.value AS Author,
                cv_artist_w.value AS Artist,
                cv_album_w.value AS Album,
                cv_year_w.value AS Year,
                cv_genre_w.value AS Genre,
                cv_series_w.value AS Series,
                cv_narrator_w.value AS Narrator,
                cv_cover_w.value AS CoverUrl,
                cv_square_w.value AS SquareUrl,
                cv_background_w.value AS BackgroundUrl,
                cv_banner_w.value AS BannerUrl,
                cv_logo_w.value AS LogoUrl,
                cv_cover_state_w.value AS CoverState,
                cv_square_state_w.value AS SquareState,
                cv_background_state_w.value AS BackgroundState,
                cv_banner_state_w.value AS BannerState,
                cv_logo_state_w.value AS LogoState,
                cv_season_a.value AS SeasonNumber,
                cv_episode_a.value AS EpisodeNumber,
                cv_track_a.value AS TrackNumber,
                cv_cover_width_w.value AS CoverWidthPx,
                cv_cover_height_w.value AS CoverHeightPx,
                cv_square_width_w.value AS SquareWidthPx,
                cv_square_height_w.value AS SquareHeightPx,
                cv_banner_width_w.value AS BannerWidthPx,
                cv_banner_height_w.value AS BannerHeightPx,
                cv_background_width_w.value AS BackgroundWidthPx,
                cv_background_height_w.value AS BackgroundHeightPx,
                cv_accent_w.value AS AccentColor
            FROM user_states us
            JOIN media_assets ma ON ma.id = us.asset_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            LEFT JOIN works pw ON pw.id = w.parent_work_id
            LEFT JOIN works gpw ON gpw.id = pw.parent_work_id
            LEFT JOIN canonical_values cv_title_a ON cv_title_a.entity_id = ma.id AND cv_title_a.key = 'title'
            LEFT JOIN canonical_values cv_title_w ON cv_title_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_title_w.key = 'title'
            LEFT JOIN canonical_values cv_author_w ON cv_author_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_author_w.key = 'author'
            LEFT JOIN canonical_values cv_artist_w ON cv_artist_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_artist_w.key = 'artist'
            LEFT JOIN canonical_values cv_album_w ON cv_album_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_album_w.key = 'album'
            LEFT JOIN canonical_values cv_year_w ON cv_year_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_year_w.key IN ('release_year', 'year')
            LEFT JOIN canonical_values cv_genre_w ON cv_genre_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_genre_w.key = 'genre'
            LEFT JOIN canonical_values cv_series_w ON cv_series_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_series_w.key = 'series'
            LEFT JOIN canonical_values cv_narrator_w ON cv_narrator_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_narrator_w.key = 'narrator'
            LEFT JOIN canonical_values cv_cover_w ON cv_cover_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_cover_w.key IN ('cover_url', 'cover')
            LEFT JOIN canonical_values cv_square_w ON cv_square_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_square_w.key IN ('square_url', 'square')
            LEFT JOIN canonical_values cv_background_w ON cv_background_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_background_w.key IN ('background_url', 'background')
            LEFT JOIN canonical_values cv_banner_w ON cv_banner_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_banner_w.key IN ('banner_url', 'banner')
            LEFT JOIN canonical_values cv_logo_w ON cv_logo_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_logo_w.key IN ('logo_url', 'logo')
            LEFT JOIN canonical_values cv_cover_state_w ON cv_cover_state_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_cover_state_w.key = 'cover_state'
            LEFT JOIN canonical_values cv_square_state_w ON cv_square_state_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_square_state_w.key = 'square_state'
            LEFT JOIN canonical_values cv_background_state_w ON cv_background_state_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_background_state_w.key = 'background_state'
            LEFT JOIN canonical_values cv_banner_state_w ON cv_banner_state_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_banner_state_w.key = 'banner_state'
            LEFT JOIN canonical_values cv_logo_state_w ON cv_logo_state_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_logo_state_w.key = 'logo_state'
            LEFT JOIN canonical_values cv_season_a ON cv_season_a.entity_id = ma.id AND cv_season_a.key = 'season_number'
            LEFT JOIN canonical_values cv_episode_a ON cv_episode_a.entity_id = ma.id AND cv_episode_a.key = 'episode_number'
            LEFT JOIN canonical_values cv_track_a ON cv_track_a.entity_id = ma.id AND cv_track_a.key = 'track_number'
            LEFT JOIN canonical_values cv_cover_width_w ON cv_cover_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_cover_width_w.key = 'cover_width_px'
            LEFT JOIN canonical_values cv_cover_height_w ON cv_cover_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_cover_height_w.key = 'cover_height_px'
            LEFT JOIN canonical_values cv_square_width_w ON cv_square_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_square_width_w.key = 'square_width_px'
            LEFT JOIN canonical_values cv_square_height_w ON cv_square_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_square_height_w.key = 'square_height_px'
            LEFT JOIN canonical_values cv_banner_width_w ON cv_banner_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_banner_width_w.key = 'banner_width_px'
            LEFT JOIN canonical_values cv_banner_height_w ON cv_banner_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_banner_height_w.key = 'banner_height_px'
            LEFT JOIN canonical_values cv_background_width_w ON cv_background_width_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_background_width_w.key = 'background_width_px'
            LEFT JOIN canonical_values cv_background_height_w ON cv_background_height_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_background_height_w.key = 'background_height_px'
            LEFT JOIN canonical_values cv_accent_w ON cv_accent_w.entity_id = COALESCE(gpw.id, pw.id, w.id) AND cv_accent_w.key = 'artwork_accent_hex'
            WHERE us.progress_pct > 0 AND us.progress_pct < 99.5
            GROUP BY us.asset_id
            ORDER BY us.last_accessed DESC;
            """;

        var rows = (await conn.QueryAsync<DisplayJourneyRow>(new CommandDefinition(sql, cancellationToken: ct))).ToList();
        foreach (var row in rows)
        {
            row.CoverUrl = ResolveArtworkUrl(row.CoverUrl, row.AssetId, "cover", row.CoverState);
            row.SquareUrl = ResolveArtworkUrl(row.SquareUrl, row.AssetId, "square", row.SquareState);
            row.BannerUrl = ResolveArtworkUrl(row.BannerUrl, row.AssetId, "banner", row.BannerState);
            row.BackgroundUrl = ResolveArtworkUrl(row.BackgroundUrl, row.AssetId, "background", row.BackgroundState);
            row.LogoUrl = ResolveArtworkUrl(row.LogoUrl, row.AssetId, "logo", row.LogoState);
        }

        return (DisplayMediaRules.NormalizeLane(lane) switch
        {
            "watch" => rows.Where(row => DisplayMediaRules.IsWatchKind(row.MediaType)),
            "read" => rows.Where(row => DisplayMediaRules.IsReadKind(row.MediaType)),
            "listen" => rows.Where(row => DisplayMediaRules.IsListenKind(row.MediaType)),
            _ => rows,
        }).ToList();
    }

    public async Task<IReadOnlySet<Guid>> LoadFavoriteWorkIdsAsync(Guid? profileId, CancellationToken ct)
    {
        if (!profileId.HasValue)
        {
            return new HashSet<Guid>();
        }

        using var conn = _db.CreateConnection();
        var ids = await conn.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT ci.work_id
            FROM collection_items ci
            INNER JOIN collections c ON c.id = ci.collection_id
            WHERE c.scope = 'user'
              AND c.profile_id = @ProfileId
              AND c.collection_type = 'Playlist'
              AND c.resolution = 'materialized'
              AND c.display_name = 'Favorites'
              AND c.is_enabled = 1;
            """,
            new { ProfileId = profileId.Value.ToString() },
            cancellationToken: ct));

        return ids.ToHashSet();
    }


    private static string? ResolveArtworkUrl(string? value, Guid assetId, string kind, string? state)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return string.Equals(state, "present", StringComparison.OrdinalIgnoreCase)
            ? $"/stream/{assetId}/{kind}"
            : null;
    }
}
