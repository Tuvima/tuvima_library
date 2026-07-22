using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public interface ICollectionBrowseReadService
{
    Task<List<CollectionDto>> GetAllAsync(CancellationToken ct);

    Task<Guid?> GetRootWorkIdAsync(Guid workId, CancellationToken ct);
    Task<Guid?> GetRepresentativeAssetIdAsync(Guid workId, CancellationToken ct);
    Task<Dictionary<Guid, Guid?>> GetPrimaryAssetIdsAsync(IEnumerable<Guid> workIds, CancellationToken ct);
    Task<CollectionPaletteReadModel?> GetAssetPaletteAsync(Guid entityId, CancellationToken ct);
    Task<IReadOnlyList<CollectionArtistWorkReadModel>> GetArtistWorksAsync(string artistName, CancellationToken ct);
    Task<IReadOnlyList<CollectionSystemViewDetailWorkReadModel>> GetSystemViewDetailWorksAsync(
        string groupField,
        string groupValue,
        string? mediaType,
        string? artistName,
        CancellationToken ct);
    IReadOnlyList<Guid> EvaluateRules(
        IReadOnlyList<CollectionRulePredicate> predicates,
        string matchMode = "all",
        string? sortField = null,
        string sortDirection = "desc",
        int limit = 0);
    Task<IReadOnlyList<string>> GetFieldValuesAsync(string field, int limit, CancellationToken ct);
    Task<List<ContentGroupDto>> GetSystemViewGroupsAsync(string? mediaType, string? groupField, CancellationToken ct);
}

public sealed record CollectionPaletteReadModel(string? PrimaryHex, string? SecondaryHex, string? AccentHex);

public sealed class CollectionArtistWorkReadModel
{
    public Guid WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public string? Title { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? TrackNumber { get; init; }
    public string? DiscNumber { get; init; }
    public string? AppleMusicId { get; init; }
    public string? ReleaseYear { get; init; }
    public string? YearValue { get; init; }
    public string? DurationSecondsValue { get; init; }
    public string? Duration { get; init; }
    public string? Runtime { get; init; }
    public string? Cover { get; init; }
    public string? Genre { get; init; }
    public string? ChildEntitiesJson { get; init; }
}

public sealed class CollectionSystemViewDetailWorkReadModel
{
    public Guid WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public Guid? RootWorkId { get; init; }
    public string? Title { get; init; }
    public string? EpisodeTitle { get; init; }
    public string? ShowName { get; init; }
    public string? SeasonNumber { get; init; }
    public string? EpisodeNumber { get; init; }
    public string? Series { get; init; }
    public string? SeriesIndex { get; init; }
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? Author { get; init; }
    public string? Director { get; init; }
    public string? TrackNumber { get; init; }
    public string? DiscNumber { get; init; }
    public string? AppleMusicId { get; init; }
    public string? ReleaseYear { get; init; }
    public string? YearValue { get; init; }
    public string? DurationSecondsValue { get; init; }
    public string? Duration { get; init; }
    public string? Runtime { get; init; }
    public string? Cover { get; init; }
    public string? Background { get; init; }
    public string? Banner { get; init; }
    public string? Hero { get; init; }
    public string? Logo { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? AccentColor { get; init; }
    public string? Genre { get; init; }
    public string? Network { get; init; }
    public string? ChildEntitiesJson { get; init; }
}

public sealed class CollectionSystemViewGroupReadModel
{
    public string GroupName { get; init; } = string.Empty;
    public int WorkCount { get; init; }
    public int DistinctTitleCount { get; init; }
    public int AlbumCount { get; init; }
    public Guid? FirstAssetId { get; init; }
    public Guid? RootWorkId { get; init; }
    public string? Creator { get; init; }
    public string? Network { get; init; }
    public string? Year { get; init; }
    public string? Description { get; init; }
    public string? Tagline { get; init; }
    public string? CoverAspectClass { get; init; }
    public string? SquareAspectClass { get; init; }
    public string? BackgroundAspectClass { get; init; }
    public string? BannerAspectClass { get; init; }
    public long? CoverWidthPx { get; init; }
    public long? CoverHeightPx { get; init; }
    public long? SquareWidthPx { get; init; }
    public long? SquareHeightPx { get; init; }
    public long? BackgroundWidthPx { get; init; }
    public long? BackgroundHeightPx { get; init; }
    public long? BannerWidthPx { get; init; }
    public long? BannerHeightPx { get; init; }
    public long? SeasonCount { get; init; }
}

public sealed class CollectionSystemViewPreviewReadModel
{
    public string GroupName { get; init; } = string.Empty;
    public Guid WorkId { get; init; }
    public Guid? AssetId { get; init; }
    public string? Title { get; init; }
    public string? Position { get; init; }
}

public sealed class CollectionBrowseReadService(
    ICollectionRepository collectionRepo,
    IPersonRepository personRepo,
    IDatabaseConnection db,
    ILogger<CollectionBrowseReadService> logger) : ICollectionBrowseReadService
{
    private readonly CollectionRuleEvaluator _ruleEvaluator = new(db);

    public async Task<List<CollectionDto>> GetAllAsync(CancellationToken ct)
    {
        var collections = await collectionRepo.GetAllAsync(ct);

        var libraryWorkIds = new HashSet<Guid>();
        using (var conn = db.CreateConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT DISTINCT e.work_id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                  AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                """;
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                libraryWorkIds.Add(GuidSql.FromDb(reader.GetValue(0)));
            }
        }

        var filtered = new List<CollectionDto>();
        foreach (var collection in collections)
        {
            var libraryWorks = collection.Works.Where(work => libraryWorkIds.Contains(work.Id)).ToList();
            if (libraryWorks.Count == 0)
            {
                continue;
            }

            var filteredCollection = new Collection
            {
                Id = collection.Id,
                UniverseId = collection.UniverseId,
                DisplayName = collection.DisplayName,
                CreatedAt = collection.CreatedAt,
                UniverseStatus = collection.UniverseStatus,
                ParentCollectionId = collection.ParentCollectionId,
                WikidataQid = collection.WikidataQid,
            };

            foreach (var work in libraryWorks)
            {
                filteredCollection.AddWork(work);
            }

            foreach (var relationship in collection.Relationships)
            {
                filteredCollection.AddRelationship(relationship);
            }

            filtered.Add(CollectionDto.FromDomain(filteredCollection));
        }

        return filtered;
    }

    public async Task<Guid?> GetRootWorkIdAsync(Guid workId, CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var value = await conn.ExecuteScalarAsync<object?>(new CommandDefinition(
            """
            SELECT COALESCE(gp.id, p.id, w.id)
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @WorkId
            LIMIT 1
            """,
            new { WorkId = workId },
            cancellationToken: ct)).ConfigureAwait(false);
        return GuidSql.FromDbNullable(value);
    }

    public async Task<Guid?> GetRepresentativeAssetIdAsync(Guid workId, CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var value = await conn.ExecuteScalarAsync<object?>(new CommandDefinition(
            """
            SELECT MIN(ma.id)
            FROM works w
            LEFT JOIN works child ON child.parent_work_id = w.id
            LEFT JOIN works grandchild ON grandchild.parent_work_id = child.id
            INNER JOIN editions e ON e.work_id IN (w.id, child.id, grandchild.id)
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE w.id = @WorkId
            """,
            new { WorkId = workId },
            cancellationToken: ct)).ConfigureAwait(false);
        return GuidSql.FromDbNullable(value);
    }

    public async Task<Dictionary<Guid, Guid?>> GetPrimaryAssetIdsAsync(
        IEnumerable<Guid> workIds,
        CancellationToken ct)
    {
        var ids = workIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<PrimaryAssetReadRow>(new CommandDefinition(
            """
            SELECT e.work_id AS WorkId, MIN(ma.id) AS AssetId
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id IN @WorkIds
            GROUP BY e.work_id
            """,
            new { WorkIds = ids.Select(GuidSql.ToBlob).ToArray() },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.ToDictionary(row => row.WorkId, row => row.AssetId);
    }

    public async Task<CollectionPaletteReadModel?> GetAssetPaletteAsync(Guid entityId, CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<CollectionPaletteReadModel>(new CommandDefinition(
            """
            SELECT primary_hex AS PrimaryHex,
                   secondary_hex AS SecondaryHex,
                   accent_hex AS AccentHex
            FROM entity_assets
            WHERE entity_id = @EntityId
              AND primary_hex IS NOT NULL
            ORDER BY is_preferred DESC, created_at DESC
            LIMIT 1
            """,
            new { EntityId = entityId },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionArtistWorkReadModel>> GetArtistWorksAsync(
        string artistName,
        CancellationToken ct)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionArtistWorkReadModel>(new CommandDefinition(
            """
            WITH artist_works AS (
                SELECT DISTINCT e.work_id
                FROM canonical_values cv
                INNER JOIN media_assets ma ON ma.id = cv.entity_id
                INNER JOIN editions e ON e.id = ma.edition_id
                WHERE cv.key = 'artist' AND cv.value = @ArtistName COLLATE NOCASE
            ),
            work_data AS (
                SELECT
                    aw.work_id AS WorkId,
                    MIN(ma.id) AS AssetId,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS Title,
                    MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS Album,
                    MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS Artist,
                    MAX(CASE WHEN cv.key = 'track_number' THEN cv.value END) AS TrackNumber,
                    MAX(CASE WHEN cv.key = 'disc_number' THEN cv.value END) AS DiscNumber,
                    MAX(CASE WHEN cv.key = 'apple_music_id' THEN cv.value END) AS AppleMusicId,
                    MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS ReleaseYear,
                    MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS YearValue,
                    MAX(CASE WHEN cv.key IN ('duration_seconds', 'duration_sec') THEN cv.value END) AS DurationSecondsValue,
                    MAX(CASE WHEN cv.key = 'duration' THEN cv.value END) AS Duration,
                    MAX(CASE WHEN cv.key = 'runtime' THEN cv.value END) AS Runtime,
                    '/stream/' || MIN(ma.id) || '/cover' AS Cover,
                    MAX(CASE WHEN cv.key = 'genre' THEN cv.value END) AS Genre,
                    MAX(CASE WHEN cv.key = 'child_entities_json' THEN cv.value END) AS ChildEntitiesJson
                FROM artist_works aw
                INNER JOIN editions e ON e.work_id = aw.work_id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                GROUP BY aw.work_id
            )
            SELECT * FROM work_data ORDER BY Album, CAST(TrackNumber AS INTEGER), Title
            """,
            new { ArtistName = artistName },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CollectionSystemViewDetailWorkReadModel>> GetSystemViewDetailWorksAsync(
        string groupField,
        string groupValue,
        string? mediaType,
        string? artistName,
        CancellationToken ct)
    {
        var sortFields = groupField.ToLowerInvariant() switch
        {
            "show_name" => "SeasonNumber, EpisodeNumber, Title",
            "artist" => "Album, CAST(TrackNumber AS INTEGER), Title",
            "album" => "CAST(TrackNumber AS INTEGER), Title",
            "series" => "CAST(SeriesIndex AS INTEGER), Title",
            _ => "Title",
        };
        var mediaTypeJoin = string.IsNullOrWhiteSpace(mediaType)
            ? "INNER JOIN works w ON w.id = e.work_id"
            : "INNER JOIN works w ON w.id = e.work_id AND w.media_type = @MediaType";
        var isMusicAlbumGroup = string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase);

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionSystemViewDetailWorkReadModel>(new CommandDefinition(
            $"""
            WITH matched_works AS (
                SELECT DISTINCT e.work_id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                {mediaTypeJoin}
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE (
                    (
                        @IsMusicAlbumGroup = 1
                        AND COALESCE(
                            (SELECT cv_parent_album.value
                             FROM canonical_values cv_parent_album
                             WHERE cv_parent_album.entity_id = COALESCE(gp.id, p.id, w.id)
                               AND cv_parent_album.key = 'album'
                             LIMIT 1),
                            (SELECT cv_parent_title.value
                             FROM canonical_values cv_parent_title
                             WHERE cv_parent_title.entity_id = COALESCE(gp.id, p.id, w.id)
                               AND cv_parent_title.key = 'title'
                             LIMIT 1),
                            (SELECT cv_asset_album.value
                             FROM canonical_values cv_asset_album
                             WHERE cv_asset_album.entity_id = ma.id
                               AND cv_asset_album.key = 'album'
                             LIMIT 1)
                        ) = @GroupValue COLLATE NOCASE
                    )
                    OR (
                        @IsMusicAlbumGroup = 0
                        AND EXISTS (
                            SELECT 1
                            FROM canonical_values cv
                            WHERE cv.key = @GroupField
                              AND cv.value = @GroupValue COLLATE NOCASE
                              AND cv.entity_id IN (ma.id, w.id, p.id, gp.id)
                        )
                    )
                )
                  AND (
                      @ArtistName IS NULL
                      OR EXISTS (
                          SELECT 1
                          FROM canonical_values cv_artist
                          WHERE cv_artist.key IN ('artist', 'author')
                            AND cv_artist.value = @ArtistName COLLATE NOCASE
                            AND cv_artist.entity_id IN (ma.id, w.id, p.id, gp.id)
                      )
                  )
            ),
            work_data AS (
                SELECT
                    mw.work_id AS WorkId,
                    MIN(ma.id) AS AssetId,
                    COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS Title,
                    MAX(CASE WHEN cv.key = 'episode_title' THEN cv.value END) AS EpisodeTitle,
                    MAX(CASE WHEN cv.key = 'show_name' THEN cv.value END) AS ShowName,
                    MAX(CASE WHEN cv.key = 'season_number' THEN cv.value END) AS SeasonNumber,
                    MAX(CASE WHEN cv.key = 'episode_number' THEN cv.value END) AS EpisodeNumber,
                    MAX(CASE WHEN cv.key = 'series' THEN cv.value END) AS Series,
                    MAX(CASE WHEN cv.key = 'series_index' THEN cv.value END) AS SeriesIndex,
                    MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS Album,
                    MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS Artist,
                    MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS Author,
                    MAX(CASE WHEN cv.key = 'director' THEN cv.value END) AS Director,
                    MAX(CASE WHEN cv.key = 'track_number' THEN cv.value END) AS TrackNumber,
                    MAX(CASE WHEN cv.key = 'disc_number' THEN cv.value END) AS DiscNumber,
                    MAX(CASE WHEN cv.key = 'apple_music_id' THEN cv.value END) AS AppleMusicId,
                    MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS ReleaseYear,
                    MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS YearValue,
                    MAX(CASE WHEN cv.key IN ('duration_seconds', 'duration_sec') THEN cv.value END) AS DurationSecondsValue,
                    MAX(CASE WHEN cv.key = 'duration' THEN cv.value END) AS Duration,
                    MAX(CASE WHEN cv.key = 'runtime' THEN cv.value END) AS Runtime,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('cover_url', 'cover', 'poster_url', 'poster') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('cover_url', 'cover', 'poster_url', 'poster') THEN NULLIF(cv.value, '') END),
                        '/stream/' || MIN(ma.id) || '/cover') AS Cover,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('background_url', 'background') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('background_url', 'background') THEN NULLIF(cv.value, '') END)) AS Background,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('banner_url', 'banner') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('banner_url', 'banner') THEN NULLIF(cv.value, '') END)) AS Banner,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('hero_url', 'hero') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('hero_url', 'hero') THEN NULLIF(cv.value, '') END)) AS Hero,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('clear_logo_url', 'clear_logo', 'logo_url', 'logo') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('clear_logo_url', 'clear_logo', 'logo_url', 'logo') THEN NULLIF(cv.value, '') END)) AS Logo,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('artwork_primary_hex', 'cover_primary_hex', 'primary_color') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('artwork_primary_hex', 'cover_primary_hex', 'primary_color') THEN NULLIF(cv.value, '') END)) AS PrimaryColor,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('artwork_secondary_hex', 'cover_secondary_hex', 'secondary_color') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('artwork_secondary_hex', 'cover_secondary_hex', 'secondary_color') THEN NULLIF(cv.value, '') END)) AS SecondaryColor,
                    COALESCE(
                        (SELECT NULLIF(value, '') FROM canonical_values
                         WHERE entity_id = COALESCE(gp.id, p.id, w.id)
                           AND key IN ('artwork_accent_hex', 'cover_accent_hex', 'accent_color', 'dominant_color') LIMIT 1),
                        MAX(CASE WHEN cv.key IN ('artwork_accent_hex', 'cover_accent_hex', 'accent_color', 'dominant_color') THEN NULLIF(cv.value, '') END)) AS AccentColor,
                    MAX(CASE WHEN cv.key = 'genre' THEN cv.value END) AS Genre,
                    MAX(CASE WHEN cv.key = 'network' THEN cv.value END) AS Network,
                    MAX(CASE WHEN cv.key = 'child_entities_json' THEN cv.value END) AS ChildEntitiesJson
                FROM matched_works mw
                INNER JOIN works w ON w.id = mw.work_id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                INNER JOIN editions e ON e.work_id = mw.work_id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN canonical_values cv ON cv.entity_id = ma.id
                GROUP BY mw.work_id
            )
            SELECT * FROM work_data ORDER BY {sortFields}
            """,
            new
            {
                GroupField = groupField,
                GroupValue = groupValue,
                ArtistName = string.IsNullOrWhiteSpace(artistName) ? null : artistName,
                IsMusicAlbumGroup = isMusicAlbumGroup ? 1 : 0,
                MediaType = mediaType,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public IReadOnlyList<Guid> EvaluateRules(
        IReadOnlyList<CollectionRulePredicate> predicates,
        string matchMode = "all",
        string? sortField = null,
        string sortDirection = "desc",
        int limit = 0) =>
        _ruleEvaluator.Evaluate(predicates, matchMode, sortField, sortDirection, limit);

    public async Task<IReadOnlyList<string>> GetFieldValuesAsync(
        string field,
        int limit,
        CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 500);
        using var conn = db.CreateConnection();
        var sql = string.Equals(field, "media_type", StringComparison.OrdinalIgnoreCase)
            ? """
              SELECT DISTINCT media_type
              FROM works
              WHERE status NOT IN ('InReview','Rejected')
              ORDER BY media_type
              LIMIT @Limit
              """
            : """
              SELECT DISTINCT value
              FROM canonical_values
              WHERE key = @Field AND value IS NOT NULL AND value != ''
              ORDER BY value
              LIMIT @Limit
              """;
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            sql,
            new { Field = field, Limit = take },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    public async Task<List<ContentGroupDto>> GetSystemViewGroupsAsync(
        string? mediaType,
        string? groupField,
        CancellationToken ct)
    {
        var definitions = BuiltInBrowseCollectionCatalog
            .GetSystemViewDefinitions(mediaType, groupField)
            .Select(definition => definition.ToCollection())
            .ToList();
        if (definitions.Count == 0)
        {
            return [];
        }

        var result = new List<ContentGroupDto>();
        foreach (var collection in definitions)
        {
            ct.ThrowIfCancellationRequested();
            var predicates = CollectionRuleEvaluator.ParseRules(collection.RuleJson);
            if (predicates.Count == 0 || string.IsNullOrWhiteSpace(collection.GroupByField))
            {
                continue;
            }

            var workIds = EvaluateRules(predicates, collection.MatchMode, collection.SortField, collection.SortDirection);
            var primaryMediaType = predicates
                .FirstOrDefault(predicate => predicate.Field.Equals("media_type", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? "Unknown";
            var rows = await QuerySystemViewGroupsAsync(workIds, collection.GroupByField, primaryMediaType, ct)
                .ConfigureAwait(false);
            var previews = await QuerySystemViewPreviewsAsync(workIds, collection.GroupByField, primaryMediaType, ct)
                .ConfigureAwait(false);
            await AddSystemViewRowsAsync(result, rows, previews, collection, primaryMediaType, collection.GroupByField, ct)
                .ConfigureAwait(false);
        }

        if (result.Count == 0
            && string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && (string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase)
                || string.Equals(groupField, "artist", StringComparison.OrdinalIgnoreCase)))
        {
            using var conn = db.CreateConnection();
            var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
            var musicWorkIds = (await conn.QueryAsync<Guid>(new CommandDefinition(
                $"""
                SELECT DISTINCT w.id
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE w.media_type = 'Music'
                  AND COALESCE(w.is_catalog_only, 0) = 0
                  AND {visibleAssetPredicate}
                """,
                cancellationToken: ct)).ConfigureAwait(false)).AsList();
            var fallbackRows = await QuerySystemViewGroupsAsync(musicWorkIds, groupField!, "Music", ct)
                .ConfigureAwait(false);
            var fallbackPreviews = await QuerySystemViewPreviewsAsync(musicWorkIds, groupField!, "Music", ct)
                .ConfigureAwait(false);
            await AddSystemViewRowsAsync(result, fallbackRows, fallbackPreviews, definitions[0], "Music", groupField!, ct)
                .ConfigureAwait(false);
        }

        logger.LogInformation(
            "Resolved {Count} system-view groups for media type {MediaType} and field {GroupField}",
            result.Count,
            mediaType ?? "(none)",
            groupField ?? "(none)");
        return result;
    }

    private async Task<IReadOnlyList<CollectionSystemViewGroupReadModel>> QuerySystemViewGroupsAsync(
        IReadOnlyList<Guid> workIds,
        string groupField,
        string primaryMediaType,
        CancellationToken ct)
    {
        if (workIds.Count == 0)
        {
            return [];
        }

        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var isMusicAlbumGroup = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase);
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionSystemViewGroupReadModel>(new CommandDefinition(
            $"""
            WITH work_assets AS (
                SELECT w.id AS WorkId,
                       ma.id AS AssetId,
                       COALESCE(gp.id, p.id, w.id) AS RootWorkId
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.id IN @WorkIds
                  AND {visibleAssetPredicate}
            ),
            resolved AS (
                SELECT wa.*,
                       CASE WHEN @IsMusicAlbumGroup = 1 THEN COALESCE(
                           (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = 'album' LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = 'title' LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = 'album' LIMIT 1))
                       ELSE COALESCE(
                           (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = @GroupField LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.WorkId AND key = @GroupField LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = @GroupField LIMIT 1))
                       END AS GroupName,
                       COALESCE(
                           (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = 'title' LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.WorkId AND key = 'title' LIMIT 1)) AS WorkTitle,
                       COALESCE(
                           (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = 'album' LIMIT 1),
                           (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = 'album' LIMIT 1)) AS AlbumName
                FROM work_assets wa
            ),
            grouped AS (
                SELECT GroupName,
                       COUNT(DISTINCT WorkId) AS WorkCount,
                       COUNT(DISTINCT COALESCE(NULLIF(WorkTitle, ''), hex(WorkId))) AS DistinctTitleCount,
                       COUNT(DISTINCT COALESCE(NULLIF(AlbumName, ''), hex(WorkId))) AS AlbumCount,
                       MIN(AssetId) AS FirstAssetId,
                       MIN(RootWorkId) AS RootWorkId
                FROM resolved
                WHERE GroupName IS NOT NULL AND TRIM(GroupName) != ''
                GROUP BY lower(GroupName)
            ),
            metadata AS (
                SELECT cv.entity_id AS EntityId,
                       MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS Artist,
                       MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS Author,
                       MAX(CASE WHEN cv.key = 'network' THEN cv.value END) AS Network,
                       MAX(CASE WHEN cv.key IN ('release_year', 'year') THEN cv.value END) AS Year,
                       MAX(CASE WHEN cv.key = 'description' THEN cv.value END) AS Description,
                       MAX(CASE WHEN cv.key = 'tagline' THEN cv.value END) AS Tagline,
                       MAX(CASE WHEN cv.key = 'cover_aspect_class' THEN cv.value END) AS CoverAspectClass,
                       MAX(CASE WHEN cv.key = 'square_aspect_class' THEN cv.value END) AS SquareAspectClass,
                       MAX(CASE WHEN cv.key = 'background_aspect_class' THEN cv.value END) AS BackgroundAspectClass,
                       MAX(CASE WHEN cv.key = 'banner_aspect_class' THEN cv.value END) AS BannerAspectClass,
                       MAX(CASE WHEN cv.key = 'cover_width_px' THEN CAST(cv.value AS INTEGER) END) AS CoverWidthPx,
                       MAX(CASE WHEN cv.key = 'cover_height_px' THEN CAST(cv.value AS INTEGER) END) AS CoverHeightPx,
                       MAX(CASE WHEN cv.key = 'square_width_px' THEN CAST(cv.value AS INTEGER) END) AS SquareWidthPx,
                       MAX(CASE WHEN cv.key = 'square_height_px' THEN CAST(cv.value AS INTEGER) END) AS SquareHeightPx,
                       MAX(CASE WHEN cv.key = 'background_width_px' THEN CAST(cv.value AS INTEGER) END) AS BackgroundWidthPx,
                       MAX(CASE WHEN cv.key = 'background_height_px' THEN CAST(cv.value AS INTEGER) END) AS BackgroundHeightPx,
                       MAX(CASE WHEN cv.key = 'banner_width_px' THEN CAST(cv.value AS INTEGER) END) AS BannerWidthPx,
                       MAX(CASE WHEN cv.key = 'banner_height_px' THEN CAST(cv.value AS INTEGER) END) AS BannerHeightPx
                FROM canonical_values cv
                WHERE cv.entity_id IN (SELECT RootWorkId FROM grouped UNION SELECT FirstAssetId FROM grouped)
                GROUP BY cv.entity_id
            )
            SELECT g.GroupName,
                   g.WorkCount,
                   g.DistinctTitleCount,
                   g.AlbumCount,
                   g.FirstAssetId,
                   g.RootWorkId,
                   COALESCE(root.Artist, root.Author, asset.Artist, asset.Author) AS Creator,
                   COALESCE(root.Network, asset.Network) AS Network,
                   COALESCE(root.Year, asset.Year) AS Year,
                   COALESCE(root.Description, asset.Description) AS Description,
                   COALESCE(root.Tagline, asset.Tagline) AS Tagline,
                   COALESCE(root.CoverAspectClass, asset.CoverAspectClass) AS CoverAspectClass,
                   COALESCE(root.SquareAspectClass, asset.SquareAspectClass) AS SquareAspectClass,
                   COALESCE(root.BackgroundAspectClass, asset.BackgroundAspectClass) AS BackgroundAspectClass,
                   COALESCE(root.BannerAspectClass, asset.BannerAspectClass) AS BannerAspectClass,
                   COALESCE(root.CoverWidthPx, asset.CoverWidthPx) AS CoverWidthPx,
                   COALESCE(root.CoverHeightPx, asset.CoverHeightPx) AS CoverHeightPx,
                   COALESCE(root.SquareWidthPx, asset.SquareWidthPx) AS SquareWidthPx,
                   COALESCE(root.SquareHeightPx, asset.SquareHeightPx) AS SquareHeightPx,
                   COALESCE(root.BackgroundWidthPx, asset.BackgroundWidthPx) AS BackgroundWidthPx,
                   COALESCE(root.BackgroundHeightPx, asset.BackgroundHeightPx) AS BackgroundHeightPx,
                   COALESCE(root.BannerWidthPx, asset.BannerWidthPx) AS BannerWidthPx,
                   COALESCE(root.BannerHeightPx, asset.BannerHeightPx) AS BannerHeightPx,
                   (SELECT COUNT(DISTINCT season.value)
                    FROM resolved r2
                    INNER JOIN canonical_values season ON season.entity_id = r2.AssetId AND season.key = 'season_number'
                    WHERE r2.RootWorkId = g.RootWorkId) AS SeasonCount
            FROM grouped g
            LEFT JOIN metadata root ON root.EntityId = g.RootWorkId
            LEFT JOIN metadata asset ON asset.EntityId = g.FirstAssetId
            ORDER BY g.GroupName
            """,
            new
            {
                WorkIds = workIds.Select(GuidSql.ToBlob).ToArray(),
                GroupField = groupField,
                IsMusicAlbumGroup = isMusicAlbumGroup ? 1 : 0,
            },
            cancellationToken: ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<ContentGroupPreviewItemDto>>> QuerySystemViewPreviewsAsync(
        IReadOnlyList<Guid> workIds,
        string groupField,
        string primaryMediaType,
        CancellationToken ct)
    {
        if (workIds.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<ContentGroupPreviewItemDto>>(StringComparer.OrdinalIgnoreCase);
        }

        var visibleAssetPredicate = HomeVisibilitySql.VisibleAssetPathPredicate("ma.file_path_root");
        var isMusicAlbumGroup = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase)
            && string.Equals(groupField, "album", StringComparison.OrdinalIgnoreCase);
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<CollectionSystemViewPreviewReadModel>(new CommandDefinition(
            $"""
            WITH work_assets AS (
                SELECT w.id AS WorkId,
                       MIN(ma.id) AS AssetId,
                       COALESCE(gp.id, p.id, w.id) AS RootWorkId
                FROM works w
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.id IN @WorkIds
                  AND {visibleAssetPredicate}
                GROUP BY w.id, COALESCE(gp.id, p.id, w.id)
            )
            SELECT CASE WHEN @IsMusicAlbumGroup = 1 THEN COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = 'album' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = 'title' LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = 'album' LIMIT 1))
                   ELSE COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = wa.RootWorkId AND key = @GroupField LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.WorkId AND key = @GroupField LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key = @GroupField LIMIT 1))
                   END AS GroupName,
                   wa.WorkId,
                   wa.AssetId,
                   COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key IN ('episode_title', 'title') ORDER BY key = 'episode_title' DESC LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.WorkId AND key = 'title' LIMIT 1),
                       'Untitled') AS Title,
                   COALESCE(
                       (SELECT value FROM canonical_values WHERE entity_id = wa.AssetId AND key IN ('series_index', 'episode_number', 'track_number') ORDER BY CASE key WHEN 'series_index' THEN 1 WHEN 'episode_number' THEN 2 ELSE 3 END LIMIT 1),
                       (SELECT value FROM canonical_values WHERE entity_id = wa.WorkId AND key = 'series_index' LIMIT 1)) AS Position
            FROM work_assets wa
            """,
            new
            {
                WorkIds = workIds.Select(GuidSql.ToBlob).ToArray(),
                GroupField = groupField,
                IsMusicAlbumGroup = isMusicAlbumGroup ? 1 : 0,
            },
            cancellationToken: ct)).ConfigureAwait(false);

        var previewShape = string.Equals(primaryMediaType, "Music", StringComparison.OrdinalIgnoreCase)
            ? "square"
            : "portrait";
        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.GroupName) && row.AssetId.HasValue)
            .GroupBy(row => row.GroupName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ContentGroupPreviewItemDto>)group
                    .OrderBy(row => ParseSequencePosition(row.Position))
                    .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
                    .Select(row => new ContentGroupPreviewItemDto(
                        row.WorkId,
                        row.Title ?? "Untitled",
                        $"/stream/{row.AssetId!.Value:D}/cover",
                        previewShape,
                        row.Position))
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task AddSystemViewRowsAsync(
        List<ContentGroupDto> result,
        IReadOnlyList<CollectionSystemViewGroupReadModel> rows,
        IReadOnlyDictionary<string, IReadOnlyList<ContentGroupPreviewItemDto>> previews,
        Collection collection,
        string primaryMediaType,
        string groupByField,
        CancellationToken ct)
    {
        var isArtistGroup = string.Equals(groupByField, "artist", StringComparison.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            string? artistPhotoUrl = null;
            Guid? artistPersonId = null;
            if (isArtistGroup)
            {
                try
                {
                    var person = await personRepo.FindByNameAsync(row.GroupName, ct).ConfigureAwait(false);
                    if (person is not null)
                    {
                        artistPersonId = person.Id;
                        if (!string.IsNullOrWhiteSpace(person.LocalHeadshotPath)
                            || !string.IsNullOrWhiteSpace(person.HeadshotUrl))
                        {
                            artistPhotoUrl = $"/persons/{person.Id}/headshot";
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Artist lookup failed for system-view group {Artist}", row.GroupName);
                }
            }

            var assetRoute = row.FirstAssetId?.ToString("D");
            result.Add(new ContentGroupDto
            {
                CollectionId = collection.Id,
                RootWorkId = row.RootWorkId,
                DisplayName = row.GroupName,
                PrimaryMediaType = primaryMediaType,
                WorkCount = row.WorkCount,
                DistinctTitleCount = row.DistinctTitleCount,
                PreviewItems = previews.GetValueOrDefault(row.GroupName) ?? [],
                CoverUrl = assetRoute is null ? null : $"/stream/{assetRoute}/cover",
                BackgroundUrl = assetRoute is null ? null : $"/stream/{assetRoute}/background",
                BannerUrl = assetRoute is null ? null : $"/stream/{assetRoute}/banner",
                LogoUrl = assetRoute is null ? null : $"/stream/{assetRoute}/logo",
                CoverAspectClass = row.CoverAspectClass,
                SquareAspectClass = row.SquareAspectClass,
                BackgroundAspectClass = row.BackgroundAspectClass,
                BannerAspectClass = row.BannerAspectClass,
                CoverWidthPx = ToInt32(row.CoverWidthPx),
                CoverHeightPx = ToInt32(row.CoverHeightPx),
                SquareWidthPx = ToInt32(row.SquareWidthPx),
                SquareHeightPx = ToInt32(row.SquareHeightPx),
                BackgroundWidthPx = ToInt32(row.BackgroundWidthPx),
                BackgroundHeightPx = ToInt32(row.BackgroundHeightPx),
                BannerWidthPx = ToInt32(row.BannerWidthPx),
                BannerHeightPx = ToInt32(row.BannerHeightPx),
                Description = row.Description,
                Tagline = row.Tagline,
                Creator = row.Creator,
                UniverseStatus = "Complete",
                CreatedAt = collection.CreatedAt,
                ArtistPhotoUrl = artistPhotoUrl,
                ArtistPersonId = artistPersonId,
                Network = row.Network,
                Year = row.Year,
                SeasonCount = row.SeasonCount is > 0 ? ToInt32(row.SeasonCount) : null,
                AlbumCount = row.AlbumCount > 0 ? row.AlbumCount : null,
            });
        }
    }

    private static int? ToInt32(long? value) =>
        value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;

    private static decimal ParseSequencePosition(string? value) =>
        decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var position)
            ? position
            : decimal.MaxValue;

    private sealed record PrimaryAssetReadRow(Guid WorkId, Guid? AssetId);
}
