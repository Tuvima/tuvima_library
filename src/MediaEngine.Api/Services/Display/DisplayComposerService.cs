using Dapper;
using MediaEngine.Contracts.Display;
using MediaEngine.Domain.Services;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Display;

public sealed class DisplayComposerService
{
    private readonly IDatabaseConnection _db;

    public DisplayComposerService(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<DisplayPageDto> BuildHomeAsync(CancellationToken ct = default)
    {
        var works = await LoadWorksAsync(ct);
        var journey = await LoadJourneyAsync(null, ct);
        var progressByWork = journey
            .GroupBy(item => item.WorkId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAccessed).First());

        var continueCards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Take(18)
            .Select(item => ToJourneyCard(item, "home"))
            .ToList();

        var freshCards = works
            .OrderByDescending(work => work.CreatedAt)
            .Take(18)
            .Select(work => ToWorkCard(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var readCards = works
            .Where(work => IsReadKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
            .Select(work => ToWorkCard(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var watchCards = works
            .Where(work => IsWatchKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
            .Select(work => ToWorkCard(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var listenCards = works
            .Where(work => IsListenKind(work.MediaType))
            .OrderByDescending(work => progressByWork.TryGetValue(work.WorkId, out var p) ? p.LastAccessed : work.CreatedAt)
            .Take(18)
            .Select(work => ToWorkCard(work, "home", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue", "Continue", "Pick up where you left off", continueCards, null);
        AddShelf(shelves, "fresh", "Fresh in your library", "Recently added across every media type", freshCards, null);
        AddShelf(shelves, "watch-next", "Watch next", "Movies and shows ready to play", watchCards, "/watch");
        AddShelf(shelves, "read-next", "Read next", "Books and comics ready to open", readCards, "/read");
        AddShelf(shelves, "listen-next", "Listen next", "Music and audiobooks ready to resume", listenCards, "/listen");

        var heroCard = continueCards.FirstOrDefault() ?? freshCards.FirstOrDefault();

        return new DisplayPageDto(
            Key: "home",
            Title: "Home",
            Subtitle: "A cross-media view of your local library",
            Hero: heroCard is null ? null : ToHero(heroCard, continueCards.Count > 0 ? "Continue with your library" : "Fresh in your library"),
            Shelves: shelves,
            Catalog: works.Select(work => ToWorkCard(work, "home", progressByWork.GetValueOrDefault(work.WorkId))).ToList());
    }

    public async Task<DisplayPageDto> BuildBrowseAsync(string? lane, string? mediaType, string? grouping, string? search, int offset, int limit, CancellationToken ct = default)
    {
        var normalizedLane = NormalizeLane(lane);
        if (normalizedLane is not null &&
            string.IsNullOrWhiteSpace(mediaType) &&
            string.IsNullOrWhiteSpace(search) &&
            (string.IsNullOrWhiteSpace(grouping) || string.Equals(grouping, "all", StringComparison.OrdinalIgnoreCase)) &&
            offset <= 0)
        {
            return await BuildLaneAsync(normalizedLane, ct);
        }

        var works = await LoadWorksAsync(ct);
        var journey = await LoadJourneyAsync(null, ct);
        var progressByWork = journey
            .GroupBy(item => item.WorkId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAccessed).First());

        var filtered = works.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            filtered = filtered.Where(work => string.Equals(NormalizeMediaType(work.MediaType), NormalizeMediaType(mediaType), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            filtered = NormalizeLane(lane) switch
            {
                "watch" => filtered.Where(work => IsWatchKind(work.MediaType)),
                "read" => filtered.Where(work => IsReadKind(work.MediaType)),
                "listen" => filtered.Where(work => IsListenKind(work.MediaType)),
                _ => filtered,
            };
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filtered = filtered.Where(work =>
                Contains(work.Title, search) ||
                Contains(work.Author, search) ||
                Contains(work.Series, search) ||
                Contains(work.Genre, search) ||
                Contains(work.Album, search) ||
                Contains(work.Artist, search));
        }

        var cards = filtered
            .OrderByDescending(work => work.CreatedAt)
            .Skip(Math.Max(0, offset))
            .Take(Math.Clamp(limit <= 0 ? 48 : limit, 1, 200))
            .Select(work => ToWorkCard(work, NormalizeLane(lane) ?? "browse", progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        return new DisplayPageDto(
            Key: $"browse-{NormalizeLane(lane) ?? "all"}-{grouping ?? "all"}",
            Title: TitleForLane(lane),
            Subtitle: null,
            Hero: cards.FirstOrDefault() is { } hero ? ToHero(hero, "From your library") : null,
            Shelves: [new DisplayShelfDto("results", "Results", null, cards, null)],
            Catalog: cards);
    }

    private async Task<DisplayPageDto> BuildLaneAsync(string lane, CancellationToken ct)
    {
        var works = await LoadWorksAsync(ct);
        var journey = await LoadJourneyAsync(lane, ct);
        var progressByWork = journey
            .GroupBy(item => item.WorkId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.LastAccessed).First());

        var laneWorks = works
            .Where(work => lane switch
            {
                "watch" => IsWatchKind(work.MediaType),
                "read" => IsReadKind(work.MediaType),
                "listen" => IsListenKind(work.MediaType),
                _ => true,
            })
            .OrderByDescending(work => work.CreatedAt)
            .ThenByDescending(work => ParseDouble(work.Year) ?? 0)
            .ToList();

        var laneJourney = journey
            .OrderByDescending(item => item.LastAccessed)
            .ToList();

        var catalog = laneWorks
            .Select(work => ToWorkCard(work, lane, progressByWork.GetValueOrDefault(work.WorkId)))
            .ToList();

        var shelves = lane switch
        {
            "watch" => BuildWatchShelves(laneWorks, laneJourney, progressByWork),
            "read" => BuildReadShelves(laneWorks, laneJourney, progressByWork),
            "listen" => BuildListenShelves(laneWorks, laneJourney, progressByWork),
            _ => [new DisplayShelfDto("results", "Results", null, catalog, null)],
        };

        var heroCard = laneJourney.FirstOrDefault() is { } journeyHero
            ? ToJourneyCard(journeyHero, lane)
            : catalog.FirstOrDefault();

        return new DisplayPageDto(
            Key: lane,
            Title: TitleForLane(lane),
            Subtitle: SubtitleForLane(lane),
            Hero: heroCard is null ? null : ToHero(heroCard, EyebrowForLane(lane, laneJourney.Count > 0)),
            Shelves: shelves,
            Catalog: catalog);
    }

    private static IReadOnlyList<DisplayShelfDto> BuildReadShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-reading", "Continue reading", "Books, comics, and audiobooks already in motion",
            journey.Take(12).Select(item => ToJourneyCard(item, "read")).ToList(), null);
        AddShelf(shelves, "reading-collections", "Collections to explore", "Series and grouped reading pulled from your library",
            BuildCollectionCards(works, "read").Take(12).ToList(), "/collections");
        AddShelf(shelves, "recently-added", "Recently added to read", "Fresh pages ready to pick up",
            works.Take(18).Select(work => ToWorkCard(work, "read", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

        var authorShelves = works
            .Where(work => !string.IsNullOrWhiteSpace(work.Author))
            .GroupBy(work => work.Author!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DisplayShelfDto(
                Key: $"author-{StableKey(group.Key)}",
                Title: group.Key,
                Subtitle: $"{group.Count()} titles",
                Items: group.Take(12).Select(work => ToWorkCard(work, "read", progressByWork.GetValueOrDefault(work.WorkId))).ToList(),
                SeeAllRoute: null));

        shelves.AddRange(authorShelves);
        return shelves;
    }

    private static IReadOnlyList<DisplayShelfDto> BuildWatchShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-watching", "Continue watching", "Movies and shows already in progress",
            journey.Take(12).Select(item => ToJourneyCard(item, "watch")).ToList(), null);
        AddShelf(shelves, "watch-collections", "Collections to watch", "Shows, series, and grouped franchises from your library",
            BuildCollectionCards(works, "watch").Take(12).ToList(), "/collections");
        AddShelf(shelves, "movies", "Movies in your library", "Feature films ready to play",
            works.Where(work => NormalizeDisplayKind(work.MediaType) == "Movie").Take(18).Select(work => ToWorkCard(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "tv", "TV in your library", "Shows and episodes ready to continue",
            works.Where(work => NormalizeDisplayKind(work.MediaType) == "TV").Take(18).Select(work => ToWorkCard(work, "watch", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);

        var genreShelves = works
            .SelectMany(work => SplitValues(work.Genre).Select(genre => (Genre: genre, Work: work)))
            .GroupBy(item => item.Genre, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() >= 3)
            .OrderByDescending(group => group.Count())
            .Take(3)
            .Select(group => new DisplayShelfDto(
                Key: $"genre-{StableKey(group.Key)}",
                Title: group.Key,
                Subtitle: $"{group.Count()} titles",
                Items: group.Take(12).Select(item => ToWorkCard(item.Work, "watch", progressByWork.GetValueOrDefault(item.Work.WorkId))).ToList(),
                SeeAllRoute: null));

        shelves.AddRange(genreShelves);
        return shelves;
    }

    private static IReadOnlyList<DisplayShelfDto> BuildListenShelves(
        IReadOnlyList<DisplayWorkRow> works,
        IReadOnlyList<DisplayJourneyRow> journey,
        IReadOnlyDictionary<Guid, DisplayJourneyRow> progressByWork)
    {
        var shelves = new List<DisplayShelfDto>();
        AddShelf(shelves, "continue-listening", "Continue listening", "Resume music and audiobooks already in progress",
            journey.Take(12).Select(item => ToJourneyCard(item, "listen")).ToList(), null);
        AddShelf(shelves, "listen-collections", "Collections and mixes", "Albums, artists, and audiobook series from your library",
            BuildCollectionCards(works, "listen").Take(12).ToList(), "/collections");
        AddShelf(shelves, "music", "New music in your library", "Album art first for recent music",
            works.Where(work => NormalizeDisplayKind(work.MediaType) == "Music").Take(18).Select(work => ToWorkCard(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        AddShelf(shelves, "audiobooks", "Audiobooks on deck", "Spoken-word titles ready to continue",
            works.Where(work => NormalizeDisplayKind(work.MediaType) == "Audiobook").Take(18).Select(work => ToWorkCard(work, "listen", progressByWork.GetValueOrDefault(work.WorkId))).ToList(), null);
        return shelves;
    }

    public async Task<DisplayPageDto> BuildContinueAsync(string? lane, int limit, CancellationToken ct = default)
    {
        var journey = await LoadJourneyAsync(NormalizeLane(lane), ct);
        var cards = journey
            .OrderByDescending(item => item.LastAccessed)
            .Take(Math.Clamp(limit <= 0 ? 24 : limit, 1, 100))
            .Select(item => ToJourneyCard(item, NormalizeLane(lane) ?? "continue"))
            .ToList();

        return new DisplayPageDto(
            Key: $"continue-{NormalizeLane(lane) ?? "all"}",
            Title: "Continue",
            Subtitle: null,
            Hero: cards.FirstOrDefault() is { } hero ? ToHero(hero, "Continue") : null,
            Shelves: [new DisplayShelfDto("continue", "Continue", null, cards, null)],
            Catalog: cards);
    }

    public Task<DisplayPageDto> BuildSearchAsync(string? query, int limit, CancellationToken ct = default) =>
        BuildBrowseAsync(null, null, "all", query, 0, limit <= 0 ? 48 : limit, ct);

    public async Task<DisplayPageDto?> BuildGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        var works = (await LoadWorksAsync(ct))
            .Where(work => work.CollectionId == groupId)
            .OrderBy(work => ParseDouble(work.SeriesPosition) ?? double.MaxValue)
            .ThenBy(work => work.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (works.Count == 0)
        {
            return null;
        }

        var cards = works.Select(work => ToWorkCard(work, "collection", null)).ToList();
        var title = works.FirstOrDefault(work => !string.IsNullOrWhiteSpace(work.Series))?.Series ?? "Collection";
        return new DisplayPageDto(
            Key: $"group-{groupId:N}",
            Title: title,
            Subtitle: $"{works.Count} items",
            Hero: cards.FirstOrDefault() is { } hero ? ToHero(hero, "Collection") : null,
            Shelves: [new DisplayShelfDto("items", title, $"{works.Count} items", cards, null)],
            Catalog: cards);
    }

    private async Task<IReadOnlyList<DisplayWorkRow>> LoadWorksAsync(CancellationToken ct)
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

    private async Task<IReadOnlyList<DisplayJourneyRow>> LoadJourneyAsync(string? lane, CancellationToken ct)
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

        return (NormalizeLane(lane) switch
        {
            "watch" => rows.Where(row => IsWatchKind(row.MediaType)),
            "read" => rows.Where(row => IsReadKind(row.MediaType)),
            "listen" => rows.Where(row => IsListenKind(row.MediaType)),
            _ => rows,
        }).ToList();
    }

    private static DisplayCardDto ToWorkCard(DisplayWorkRow row, string context, DisplayJourneyRow? progress)
    {
        var mediaKind = NormalizeDisplayKind(row.MediaType);
        var action = PrimaryAction(row.AssetId, row.WorkId, row.CollectionId, mediaKind, progress?.ProgressPct);
        var progressDto = progress is null ? null : ToProgress(progress, action);
        return new DisplayCardDto(
            Id: row.WorkId,
            WorkId: row.WorkId,
            AssetId: row.AssetId,
            CollectionId: row.CollectionId,
            MediaType: mediaKind,
            GroupingType: "work",
            Title: row.Title,
            Subtitle: CreatorFor(row),
            Facts: BuildFacts(mediaKind, row.Title, row.Year, row.Genre, row.Author, row.Artist, row.Narrator, row.Series, row.SeasonNumber, row.EpisodeNumber, row.TrackNumber, row.Album),
            Artwork: ArtworkFor(row),
            PreferredShape: PreferredShape(row.MediaType, row.BackgroundUrl, row.BannerUrl, row.SquareUrl),
            Presentation: PresentationFor(mediaKind),
            TileTextMode: string.Equals(context, "home", StringComparison.OrdinalIgnoreCase) ? "coverOnly" : "caption",
            PreviewPlacement: IsReadKind(row.MediaType) ? "bottom" : "smart",
            Progress: progressDto,
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.CreatedAt);
    }

    private static DisplayCardDto ToJourneyCard(DisplayJourneyRow row, string context)
    {
        var mediaKind = NormalizeDisplayKind(row.MediaType);
        var action = PrimaryAction(row.AssetId, row.WorkId, row.CollectionId, mediaKind, row.ProgressPct);
        return new DisplayCardDto(
            Id: row.WorkId,
            WorkId: row.WorkId,
            AssetId: row.AssetId,
            CollectionId: row.CollectionId,
            MediaType: mediaKind,
            GroupingType: "work",
            Title: row.Title,
            Subtitle: FirstNonBlank(row.Author, row.Artist, row.Series),
            Facts: BuildFacts(mediaKind, row.Title, row.Year, row.Genre, row.Author, row.Artist, row.Narrator, row.Series, row.SeasonNumber, row.EpisodeNumber, row.TrackNumber, row.Album),
            Artwork: ArtworkFor(row),
            PreferredShape: PreferredShape(row.MediaType, row.BackgroundUrl, row.BannerUrl, row.SquareUrl),
            Presentation: PresentationFor(mediaKind),
            TileTextMode: string.Equals(context, "home", StringComparison.OrdinalIgnoreCase) ? "coverOnly" : "caption",
            PreviewPlacement: IsReadKind(row.MediaType) ? "bottom" : "smart",
            Progress: ToProgress(row, action),
            Actions: [action, DetailsAction(row.WorkId, row.CollectionId, mediaKind)],
            Flags: FlagsFor(row.MediaType, isCollection: false),
            SortTimestamp: row.LastAccessed);
    }

    private static IReadOnlyList<DisplayCardDto> BuildCollectionCards(IReadOnlyList<DisplayWorkRow> works, string lane) =>
        works
            .Where(work => work.CollectionId.HasValue)
            .GroupBy(work => work.CollectionId!.Value)
            .Select(group => ToCollectionCard(group.Key, group.ToList(), lane))
            .Where(card => card is not null)
            .Cast<DisplayCardDto>()
            .OrderByDescending(card => card.SortTimestamp)
            .ToList();

    private static DisplayCardDto? ToCollectionCard(Guid collectionId, IReadOnlyList<DisplayWorkRow> works, string lane)
    {
        if (works.Count == 0)
        {
            return null;
        }

        var representative = works
            .OrderByDescending(work => !string.IsNullOrWhiteSpace(work.BackgroundUrl) || !string.IsNullOrWhiteSpace(work.BannerUrl))
            .ThenByDescending(work => !string.IsNullOrWhiteSpace(work.CoverUrl) || !string.IsNullOrWhiteSpace(work.SquareUrl))
            .ThenByDescending(work => work.CreatedAt)
            .First();
        var mediaKind = NormalizeDisplayKind(representative.MediaType);
        var title = FirstNonBlank(representative.ShowName, representative.Series, representative.Album, representative.Artist, representative.Title) ?? "Collection";
        var action = new DisplayActionDto("openCollection", "Open", null, null, collectionId, $"/collection/{collectionId}");

        return new DisplayCardDto(
            Id: collectionId,
            WorkId: null,
            AssetId: null,
            CollectionId: collectionId,
            MediaType: mediaKind,
            GroupingType: "collection",
            Title: title,
            Subtitle: $"{works.Count} titles",
            Facts: CollectionFacts(mediaKind, works.Count, representative.Genre),
            Artwork: ArtworkFor(representative),
            PreferredShape: CollectionShape(lane, mediaKind, representative),
            Presentation: CollectionPresentation(lane, mediaKind),
            TileTextMode: "caption",
            PreviewPlacement: "smart",
            Progress: null,
            Actions: [action],
            Flags: FlagsFor(representative.MediaType, isCollection: true),
            SortTimestamp: works.Max(work => work.CreatedAt));
    }

    private static DisplayHeroDto ToHero(DisplayCardDto card, string eyebrow) =>
        new(card.Title, card.Subtitle, eyebrow, card.Artwork, card.Progress, card.Actions);

    private static DisplayProgressDto ToProgress(DisplayJourneyRow row, DisplayActionDto resumeAction) =>
        new(Math.Clamp(row.ProgressPct, 0, 100), $"{Math.Max(1, row.ProgressPct):F0}%", row.LastAccessed, resumeAction);

    private static void AddShelf(List<DisplayShelfDto> shelves, string key, string title, string subtitle, IReadOnlyList<DisplayCardDto> cards, string? route)
    {
        if (cards.Count == 0)
        {
            return;
        }

        shelves.Add(new DisplayShelfDto(key, title, subtitle, cards, route));
    }

    private static DisplayActionDto PrimaryAction(Guid? assetId, Guid workId, Guid? collectionId, string mediaKind, double? progressPct)
    {
        var isContinue = progressPct is > 0 and < 99.5;
        if (mediaKind is "Book" or "Comic")
        {
            return new DisplayActionDto("readAsset", isContinue ? "Continue Reading" : "Read", workId, assetId, collectionId, assetId.HasValue ? $"/read/{assetId}" : $"/book/{workId}");
        }

        if (mediaKind is "Movie" or "TV" or "Music" or "Audiobook")
        {
            return new DisplayActionDto("playAsset", isContinue ? ContinueLabel(mediaKind) : "Play", workId, assetId, collectionId, WebUrlFor(workId, collectionId, mediaKind));
        }

        return new DisplayActionDto("openWork", "Open", workId, assetId, collectionId, WebUrlFor(workId, collectionId, mediaKind));
    }

    private static DisplayActionDto DetailsAction(Guid workId, Guid? collectionId, string mediaKind) =>
        new("openWork", "Details", workId, null, collectionId, WebUrlFor(workId, collectionId, mediaKind));

    private static string WebUrlFor(Guid workId, Guid? collectionId, string mediaKind)
    {
        if (collectionId.HasValue)
        {
            return mediaKind switch
            {
                "Movie" => $"/watch/movies/{workId}",
                "TV" => $"/watch/tv/{collectionId}",
                "Music" => $"/listen/music/albums/{collectionId}",
                _ => $"/collection/{collectionId}",
            };
        }

        return mediaKind switch
        {
            "Movie" => $"/watch/movies/{workId}",
            "TV" => $"/watch/tv/episodes/{workId}",
            "Music" => $"/listen/music/tracks/{workId}",
            "Audiobook" => $"/book/{workId}",
            "Comic" => $"/book/{workId}",
            "Book" => $"/book/{workId}",
            _ => $"/book/{workId}",
        };
    }

    private static DisplayCardFlagsDto FlagsFor(string mediaType, bool isCollection) =>
        new(IsWatchKind(mediaType) || IsListenKind(mediaType), IsReadKind(mediaType), !isCollection, isCollection, false);

    private static DisplayArtworkDto ArtworkFor(IDisplayArtworkRow row) =>
        new(row.CoverUrl, row.SquareUrl, row.BannerUrl, row.BackgroundUrl, row.LogoUrl, ParseInt(row.CoverWidthPx), ParseInt(row.CoverHeightPx), ParseInt(row.SquareWidthPx), ParseInt(row.SquareHeightPx), ParseInt(row.BannerWidthPx), ParseInt(row.BannerHeightPx), ParseInt(row.BackgroundWidthPx), ParseInt(row.BackgroundHeightPx), row.AccentColor);

    private static IReadOnlyList<string> BuildFacts(string mediaKind, string title, string? year, string? genre, string? author, string? artist, string? narrator, string? series, string? season, string? episode, string? track, string? album)
        => DisplayFactBuilder.Build(mediaKind, title, year, genre, author, artist, narrator, series, season, episode, track, album);

    private static string PreferredShape(string mediaType, string? backgroundUrl, string? bannerUrl, string? squareUrl)
    {
        if (!string.IsNullOrWhiteSpace(backgroundUrl) || !string.IsNullOrWhiteSpace(bannerUrl))
        {
            return "landscape";
        }

        if (NormalizeDisplayKind(mediaType) is "Music" or "Audiobook" || !string.IsNullOrWhiteSpace(squareUrl))
        {
            return "square";
        }

        return "portrait";
    }

    private static string PresentationFor(string mediaKind) => mediaKind switch
    {
        "TV" => "tvSeries",
        "Movie" => "movie",
        "Music" => "album",
        "Audiobook" => "audiobook",
        "Comic" => "comic",
        "Book" => "book",
        _ => "default",
    };

    private static string? NormalizeLane(string? lane)
    {
        if (string.IsNullOrWhiteSpace(lane))
        {
            return null;
        }

        var normalized = lane.Trim().ToLowerInvariant();
        return normalized is "watch" or "read" or "listen" ? normalized : null;
    }

    private static string TitleForLane(string? lane) => NormalizeLane(lane) switch
    {
        "watch" => "Watch",
        "read" => "Read",
        "listen" => "Listen",
        _ => "Browse",
    };

    private static string SubtitleForLane(string lane) => lane switch
    {
        "watch" => "Movies and shows from your local library",
        "read" => "Books, comics, and audiobooks from your local library",
        "listen" => "Music and audiobooks from your local library",
        _ => "Browse your local library",
    };

    private static string EyebrowForLane(string lane, bool hasProgress) => lane switch
    {
        "watch" => hasProgress ? "Continue watching" : "Featured from your library",
        "read" => hasProgress ? "Continue reading" : "New on your shelf",
        "listen" => hasProgress ? "Continue listening" : "Featured from your library",
        _ => "From your library",
    };

    private static string CollectionShape(string lane, string mediaKind, DisplayWorkRow representative)
    {
        if (lane == "watch")
        {
            return "landscape";
        }

        if (lane == "listen" || mediaKind is "Music" or "Audiobook")
        {
            return "square";
        }

        return PreferredShape(representative.MediaType, representative.BackgroundUrl, representative.BannerUrl, representative.SquareUrl);
    }

    private static string CollectionPresentation(string lane, string mediaKind) => mediaKind switch
    {
        "TV" => "tvSeries",
        "Movie" => "movieSeries",
        "Book" => "bookSeries",
        "Comic" => "comicSeries",
        "Audiobook" => "audiobookSeries",
        "Music" when lane == "listen" => "album",
        "Music" => "album",
        _ => "default",
    };

    private static IReadOnlyList<string> CollectionFacts(string mediaKind, int count, string? genre)
    {
        var facts = new List<string> { $"{count} titles" };
        facts.AddRange(SplitValues(genre).Where(value => !string.Equals(value, mediaKind, StringComparison.OrdinalIgnoreCase)).Take(2));
        return facts;
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMediaType(string mediaType) => NormalizeDisplayKind(mediaType) switch
    {
        "Movie" => "Movies",
        "TV" => "TV",
        "Book" => "Books",
        "Comic" => "Comics",
        "Audiobook" => "Audiobooks",
        "Music" => "Music",
        var value => value,
    };

    private static string NormalizeDisplayKind(string? mediaType)
    {
        var label = MediaTypeClassifier.GetDisplayLabel(mediaType ?? string.Empty);
        return label switch
        {
            "Movies" => "Movie",
            "Books" => "Book",
            "Comics" => "Comic",
            "Audiobooks" => "Audiobook",
            _ => label,
        };
    }

    private static bool IsWatchKind(string mediaType) => NormalizeDisplayKind(mediaType) is "Movie" or "TV";
    private static bool IsReadKind(string mediaType) => NormalizeDisplayKind(mediaType) is "Book" or "Comic" or "Audiobook";
    private static bool IsListenKind(string mediaType) => NormalizeDisplayKind(mediaType) is "Music" or "Audiobook";

    private static IReadOnlyList<string> SplitValues(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .Split(['|', ';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string StableKey(string value)
    {
        var chars = value.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var key = new string(chars).Trim('-');
        while (key.Contains("--", StringComparison.Ordinal))
        {
            key = key.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(key) ? "group" : key;
    }

    private static string ContinueLabel(string mediaKind) => mediaKind switch
    {
        "Movie" or "TV" => "Continue Watching",
        "Book" or "Comic" => "Continue Reading",
        "Music" or "Audiobook" => "Continue Listening",
        _ => "Continue",
    };

    private static string? CreatorFor(DisplayWorkRow row) =>
        FirstNonBlank(row.Author, row.Artist, row.Director, row.Narrator);

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : null;

    private static double? ParseDouble(string? value) =>
        double.TryParse(value, out var parsed) ? parsed : null;

    private interface IDisplayArtworkRow
    {
        string? CoverUrl { get; }
        string? SquareUrl { get; }
        string? BannerUrl { get; }
        string? BackgroundUrl { get; }
        string? LogoUrl { get; }
        string? CoverWidthPx { get; }
        string? CoverHeightPx { get; }
        string? SquareWidthPx { get; }
        string? SquareHeightPx { get; }
        string? BannerWidthPx { get; }
        string? BannerHeightPx { get; }
        string? BackgroundWidthPx { get; }
        string? BackgroundHeightPx { get; }
        string? AccentColor { get; }
    }

    private sealed class DisplayWorkRow : IDisplayArtworkRow
    {
        public Guid WorkId { get; set; }
        public Guid? CollectionId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public string? WorkKind { get; set; }
        public Guid RootWorkId { get; set; }
        public Guid AssetId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Year { get; set; }
        public string? Genre { get; set; }
        public string? Series { get; set; }
        public string? SeriesPosition { get; set; }
        public string? Narrator { get; set; }
        public string? Director { get; set; }
        public string? Network { get; set; }
        public string? ShowName { get; set; }
        public string? SeasonNumber { get; set; }
        public string? EpisodeNumber { get; set; }
        public string? TrackNumber { get; set; }
        public string? CoverUrl { get; set; }
        public string? SquareUrl { get; set; }
        public string? BannerUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        public string? LogoUrl { get; set; }
        public string? CoverState { get; set; }
        public string? SquareState { get; set; }
        public string? BannerState { get; set; }
        public string? BackgroundState { get; set; }
        public string? LogoState { get; set; }
        public string? CoverWidthPx { get; set; }
        public string? CoverHeightPx { get; set; }
        public string? SquareWidthPx { get; set; }
        public string? SquareHeightPx { get; set; }
        public string? BannerWidthPx { get; set; }
        public string? BannerHeightPx { get; set; }
        public string? BackgroundWidthPx { get; set; }
        public string? BackgroundHeightPx { get; set; }
        public string? AccentColor { get; set; }
    }

    private sealed class DisplayJourneyRow : IDisplayArtworkRow
    {
        public Guid AssetId { get; set; }
        public Guid WorkId { get; set; }
        public Guid? CollectionId { get; set; }
        public string MediaType { get; set; } = string.Empty;
        public double ProgressPct { get; set; }
        public DateTimeOffset LastAccessed { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Author { get; set; }
        public string? Artist { get; set; }
        public string? Album { get; set; }
        public string? Year { get; set; }
        public string? Genre { get; set; }
        public string? Series { get; set; }
        public string? Narrator { get; set; }
        public string? SeasonNumber { get; set; }
        public string? EpisodeNumber { get; set; }
        public string? TrackNumber { get; set; }
        public string? CoverUrl { get; set; }
        public string? SquareUrl { get; set; }
        public string? BannerUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        public string? LogoUrl { get; set; }
        public string? CoverState { get; set; }
        public string? SquareState { get; set; }
        public string? BannerState { get; set; }
        public string? BackgroundState { get; set; }
        public string? LogoState { get; set; }
        public string? CoverWidthPx { get; set; }
        public string? CoverHeightPx { get; set; }
        public string? SquareWidthPx { get; set; }
        public string? SquareHeightPx { get; set; }
        public string? BannerWidthPx { get; set; }
        public string? BannerHeightPx { get; set; }
        public string? BackgroundWidthPx { get; set; }
        public string? BackgroundHeightPx { get; set; }
        public string? AccentColor { get; set; }
    }
}
