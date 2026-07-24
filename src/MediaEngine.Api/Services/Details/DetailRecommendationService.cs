using System.Globalization;
using System.Text;
using Dapper;
using MediaEngine.Api.Services.Display;
using MediaEngine.Contracts.Details;
using MediaEngine.Storage;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.Details;

/// <summary>
/// Reads and projects the scored "more like this" shelf for a detail page.
/// </summary>
public sealed class DetailRecommendationService
{
    private readonly IDatabaseConnection _db;

    public DetailRecommendationService(IDatabaseConnection db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<MediaGroupingItemViewModel>> LoadAsync(
        Guid workId,
        DetailEntityType entityType,
        CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync(new CommandDefinition(
            """
            WITH current_work AS (
                SELECT w.id AS LeafWorkId,
                       p.id AS ParentWorkId,
                       gp.id AS GrandParentWorkId,
                       COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                       COALESCE(gp.media_type, p.media_type, w.media_type) AS RootMediaType,
                       w.media_type AS LeafMediaType
                FROM works w
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.id = @workId
                LIMIT 1
            ),
            current_lineage AS (
                SELECT LeafWorkId AS EntityId FROM current_work
                UNION
                SELECT ParentWorkId FROM current_work WHERE ParentWorkId IS NOT NULL
                UNION
                SELECT GrandParentWorkId FROM current_work WHERE GrandParentWorkId IS NOT NULL
                UNION
                SELECT RootWorkId FROM current_work WHERE RootWorkId IS NOT NULL
            ),
            current_assets AS (
                SELECT ma.id AS EntityId
                FROM current_lineage cl
                INNER JOIN editions e ON e.work_id = cl.EntityId
                INNER JOIN media_assets ma ON ma.edition_id = e.id
            ),
            target_entities AS (
                SELECT EntityId FROM current_lineage
                UNION
                SELECT EntityId FROM current_assets
            ),
            target_media AS (
                SELECT LOWER(COALESCE(RootMediaType, LeafMediaType, '')) AS MediaType
                FROM current_work
                LIMIT 1
            ),
            target_signals AS (
                SELECT DISTINCT
                       CASE
                           WHEN cv.key IN ('cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator') THEN 'person'
                           WHEN cv.key IN ('mood', 'vibe') THEN 'mood'
                           ELSE cv.key
                       END AS SignalKey,
                       LOWER(TRIM(cv.value)) AS SignalValue
                FROM canonical_values cv
                WHERE cv.entity_id IN (SELECT EntityId FROM target_entities)
                  AND cv.key IN ('genre', 'cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator', 'themes', 'mood', 'vibe', 'custom_tags')
                  AND NULLIF(TRIM(cv.value), '') IS NOT NULL
                UNION
                SELECT DISTINCT
                       CASE
                           WHEN cva.key IN ('cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator') THEN 'person'
                           WHEN cva.key IN ('mood', 'vibe') THEN 'mood'
                           ELSE cva.key
                       END AS SignalKey,
                       LOWER(TRIM(cva.value)) AS SignalValue
                FROM canonical_value_arrays cva
                WHERE cva.entity_id IN (SELECT EntityId FROM target_entities)
                  AND cva.key IN ('genre', 'cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator', 'themes', 'mood', 'vibe', 'custom_tags')
                  AND NULLIF(TRIM(cva.value), '') IS NOT NULL
            ),
            target_year AS (
                SELECT CAST(SUBSTR(cv.value, 1, 4) AS INTEGER) AS YearValue
                FROM canonical_values cv
                WHERE cv.entity_id IN (SELECT EntityId FROM target_entities)
                  AND cv.key IN ('year', 'release_year', 'first_air_date', 'release_date')
                  AND cv.value GLOB '[0-9][0-9][0-9][0-9]*'
                ORDER BY cv.entity_id IN (SELECT RootWorkId FROM current_work) DESC
                LIMIT 1
            ),
            work_units AS (
                SELECT w.id AS LeafWorkId,
                       w.collection_id AS LeafCollectionId,
                       w.media_type AS LeafMediaType,
                       COALESCE(gp.id, p.id, w.id) AS RootWorkId,
                       COALESCE(gp.collection_id, p.collection_id, w.collection_id) AS RootCollectionId,
                       COALESCE(gp.media_type, p.media_type, w.media_type) AS RootMediaType,
                       COALESCE(w.ownership, 'Owned') AS Ownership,
                       COALESCE(w.is_catalog_only, 0) AS IsCatalogOnly,
                       MIN(ma.id) AS RepresentativeAssetId
                FROM works w
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                LEFT JOIN editions e ON e.work_id = w.id
                LEFT JOIN media_assets ma ON ma.edition_id = e.id
                GROUP BY w.id
            ),
            candidate_units AS (
                SELECT
                    CASE
                        WHEN LOWER(RootMediaType) LIKE '%tv%' OR LOWER(LeafMediaType) LIKE '%tv%' THEN RootWorkId
                        ELSE LeafWorkId
                    END AS CandidateWorkId,
                    CASE
                        WHEN LOWER(RootMediaType) LIKE '%tv%' OR LOWER(LeafMediaType) LIKE '%tv%' THEN RootCollectionId
                        ELSE LeafCollectionId
                    END AS CandidateCollectionId,
                    CASE
                        WHEN LOWER(RootMediaType) LIKE '%tv%' OR LOWER(LeafMediaType) LIKE '%tv%' THEN RootMediaType
                        ELSE LeafMediaType
                    END AS CandidateMediaType,
                    RepresentativeAssetId
                FROM work_units
                WHERE RepresentativeAssetId IS NOT NULL
                  AND Ownership = 'Owned'
                  AND IsCatalogOnly = 0
            ),
            candidate_base AS (
                SELECT CandidateWorkId,
                       MAX(CandidateCollectionId) AS CandidateCollectionId,
                       MAX(CandidateMediaType) AS CandidateMediaType,
                       MIN(RepresentativeAssetId) AS RepresentativeAssetId
                FROM candidate_units
                WHERE CandidateWorkId NOT IN (SELECT EntityId FROM current_lineage)
                GROUP BY CandidateWorkId
            ),
            candidate_entities AS (
                SELECT CandidateWorkId, CandidateWorkId AS EntityId FROM candidate_base
                UNION
                SELECT CandidateWorkId, RepresentativeAssetId FROM candidate_base WHERE RepresentativeAssetId IS NOT NULL
            ),
            candidate_signals AS (
                SELECT DISTINCT
                       ce.CandidateWorkId,
                       CASE
                           WHEN cv.key IN ('cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator') THEN 'person'
                           WHEN cv.key IN ('mood', 'vibe') THEN 'mood'
                           ELSE cv.key
                       END AS SignalKey,
                       LOWER(TRIM(cv.value)) AS SignalValue
                FROM candidate_entities ce
                INNER JOIN canonical_values cv ON cv.entity_id = ce.EntityId
                WHERE cv.key IN ('genre', 'cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator', 'themes', 'mood', 'vibe', 'custom_tags')
                  AND NULLIF(TRIM(cv.value), '') IS NOT NULL
                UNION
                SELECT DISTINCT
                       ce.CandidateWorkId,
                       CASE
                           WHEN cva.key IN ('cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator') THEN 'person'
                           WHEN cva.key IN ('mood', 'vibe') THEN 'mood'
                           ELSE cva.key
                       END AS SignalKey,
                       LOWER(TRIM(cva.value)) AS SignalValue
                FROM candidate_entities ce
                INNER JOIN canonical_value_arrays cva ON cva.entity_id = ce.EntityId
                WHERE cva.key IN ('genre', 'cast_member', 'voice_actor', 'director', 'author', 'narrator', 'artist', 'album_artist', 'composer', 'screenwriter', 'illustrator', 'themes', 'mood', 'vibe', 'custom_tags')
                  AND NULLIF(TRIM(cva.value), '') IS NOT NULL
            ),
            signal_scores AS (
                SELECT cs.CandidateWorkId,
                       SUM(CASE cs.SignalKey
                           WHEN 'person' THEN 4.0
                           WHEN 'genre' THEN 3.0
                           WHEN 'themes' THEN 2.0
                           WHEN 'mood' THEN 1.5
                           WHEN 'custom_tags' THEN 1.5
                           ELSE 1.0
                       END) AS SignalScore,
                       SUM(CASE WHEN cs.SignalKey = 'person' THEN 1 ELSE 0 END) AS MatchedPeople,
                       SUM(CASE WHEN cs.SignalKey = 'genre' THEN 1 ELSE 0 END) AS MatchedGenres,
                       SUM(CASE WHEN cs.SignalKey IN ('themes', 'mood') THEN 1 ELSE 0 END) AS MatchedAi,
                       GROUP_CONCAT(DISTINCT cs.SignalValue) AS MatchedSignals
                FROM candidate_signals cs
                INNER JOIN target_signals ts
                    ON ts.SignalKey = cs.SignalKey
                   AND ts.SignalValue = cs.SignalValue
                GROUP BY cs.CandidateWorkId
            ),
            candidate_year AS (
                SELECT ce.CandidateWorkId,
                       CAST(SUBSTR(cv.value, 1, 4) AS INTEGER) AS YearValue
                FROM candidate_entities ce
                INNER JOIN canonical_values cv ON cv.entity_id = ce.EntityId
                WHERE cv.key IN ('year', 'release_year', 'first_air_date', 'release_date')
                  AND cv.value GLOB '[0-9][0-9][0-9][0-9]*'
                GROUP BY ce.CandidateWorkId
            ),
            scored AS (
                SELECT cb.CandidateWorkId AS WorkId,
                       cb.CandidateCollectionId AS CollectionId,
                       cb.CandidateMediaType AS MediaType,
                       cb.RepresentativeAssetId AS AssetId,
                       CAST(COALESCE(
                           CASE WHEN LOWER(COALESCE(cb.CandidateMediaType, '')) LIKE '%tv%' THEN c.display_name END,
                           (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = cb.CandidateWorkId AND key IN ('show_name', 'title', 'album') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL ORDER BY CASE key WHEN 'show_name' THEN 0 WHEN 'title' THEN 1 ELSE 2 END LIMIT 1),
                           'Untitled'
                       ) AS TEXT) AS Title,
                       CAST((SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id IN (cb.CandidateWorkId, cb.RepresentativeAssetId) AND key IN ('description', 'overview', 'plot_summary') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS TEXT) AS Description,
                       CAST(COALESCE(
                           (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = cb.CandidateWorkId AND key IN ('background_url', 'background', 'hero_url', 'hero', 'poster_url', 'poster', 'cover_url', 'cover') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1),
                           (SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = cb.RepresentativeAssetId AND key IN ('background_url', 'background', 'hero_url', 'hero', 'poster_url', 'poster', 'cover_url', 'cover') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1)
                       ) AS TEXT) AS ArtworkUrl,
                       CAST((SELECT NULLIF(CAST(value AS TEXT), '') FROM canonical_values WHERE entity_id = cb.RepresentativeAssetId AND key IN ('background_state', 'hero_state', 'cover_state') AND NULLIF(CAST(value AS TEXT), '') IS NOT NULL LIMIT 1) AS TEXT) AS ArtworkState,
                       CAST(cy.YearValue AS TEXT) AS Year,
                       COALESCE(ss.SignalScore, 0.0) AS SignalScore,
                       COALESCE(ss.MatchedPeople, 0) AS MatchedPeople,
                       COALESCE(ss.MatchedGenres, 0) AS MatchedGenres,
                       COALESCE(ss.MatchedAi, 0) AS MatchedAi,
                       CASE
                           WHEN (SELECT YearValue FROM target_year LIMIT 1) IS NULL OR cy.YearValue IS NULL THEN 0.0
                           WHEN ABS(cy.YearValue - (SELECT YearValue FROM target_year LIMIT 1)) = 0 THEN 2.0
                           WHEN ABS(cy.YearValue - (SELECT YearValue FROM target_year LIMIT 1)) <= 2 THEN 1.25
                           WHEN ABS(cy.YearValue - (SELECT YearValue FROM target_year LIMIT 1)) <= 5 THEN 0.5
                           ELSE 0.0
                       END AS YearScore,
                       CASE
                           WHEN LOWER(COALESCE(cb.CandidateMediaType, '')) = (SELECT MediaType FROM target_media LIMIT 1) THEN 0.5
                           ELSE 0.0
                       END AS MediaTypeScore,
                       ss.MatchedSignals AS MatchedSignals
                FROM candidate_base cb
                LEFT JOIN collections c ON c.id = cb.CandidateCollectionId
                LEFT JOIN signal_scores ss ON ss.CandidateWorkId = cb.CandidateWorkId
                LEFT JOIN candidate_year cy ON cy.CandidateWorkId = cb.CandidateWorkId
                WHERE NOT (LOWER(COALESCE(cb.CandidateMediaType, '')) LIKE '%tv%' AND cb.CandidateCollectionId IS NULL)
            )
            SELECT *,
                   SignalScore + YearScore + MediaTypeScore AS Score
            FROM scored
            WHERE SignalScore + YearScore + MediaTypeScore > 1.0
            ORDER BY Score DESC, Title COLLATE NOCASE
            LIMIT 8;
            """,
            new { workId },
            cancellationToken: ct)))
            .Select(row => new RecommendationWorkRow
            {
                WorkId = StringValue(row.WorkId) ?? string.Empty,
                CollectionId = StringValue(row.CollectionId),
                MediaType = StringValue(row.MediaType),
                AssetId = StringValue(row.AssetId),
                Title = StringValue(row.Title) ?? "Untitled",
                Description = StringValue(row.Description),
                ArtworkUrl = StringValue(row.ArtworkUrl),
                ArtworkState = StringValue(row.ArtworkState),
                Year = StringValue(row.Year),
                SignalScore = DoubleValue(row.SignalScore) ?? 0,
                MatchedPeople = IntValue(row.MatchedPeople) ?? 0,
                MatchedGenres = IntValue(row.MatchedGenres) ?? 0,
                MatchedAi = IntValue(row.MatchedAi) ?? 0,
                YearScore = DoubleValue(row.YearScore) ?? 0,
                MediaTypeScore = DoubleValue(row.MediaTypeScore) ?? 0,
                MatchedSignals = StringValue(row.MatchedSignals),
                Score = DoubleValue(row.Score) ?? 0,
            })
            .ToList();

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.WorkId) && !string.IsNullOrWhiteSpace(row.Title))
            .Select(ToRecommendationItem)
            .Where(item => item.EntityType is not DetailEntityType.TvEpisode)
            .ToList();
    }

    private static MediaGroupingItemViewModel ToRecommendationItem(RecommendationWorkRow row)
    {
        var entityType = InferRecommendationEntityType(row.MediaType);
        var route = BuildRecommendationRoute(row, entityType);
        var artworkUrl = ResolveCollectionArtworkUrl(row.ArtworkUrl, row.AssetId, "cover", row.ArtworkState);

        return new MediaGroupingItemViewModel
        {
            Id = entityType == DetailEntityType.TvShow && !string.IsNullOrWhiteSpace(row.CollectionId)
                ? row.CollectionId!
                : row.WorkId,
            EntityType = entityType,
            Title = row.Title,
            Subtitle = BuildRecommendationReason(row),
            Description = row.Description,
            ArtworkUrl = artworkUrl,
            Metadata = BuildRecommendationMetadata(row),
            Actions = string.IsNullOrWhiteSpace(route)
                ? []
                : [new DetailAction { Key = "open", Label = "Open", Icon = "open_in_new", Route = route }],
            IsOwned = true,
        };
    }

    private static DetailEntityType InferRecommendationEntityType(string? mediaType)
    {
        if (mediaType?.Contains("tv", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.TvShow;
        }

        if (mediaType?.Contains("movie", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.Movie;
        }

        if (mediaType?.Contains("music", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.MusicTrack;
        }

        if (mediaType?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.Audiobook;
        }

        if (mediaType?.Contains("comic", StringComparison.OrdinalIgnoreCase) == true)
        {
            return DetailEntityType.ComicIssue;
        }

        return DetailEntityType.Book;
    }

    private static string? BuildRecommendationRoute(RecommendationWorkRow row, DetailEntityType entityType)
    {
        var workId = row.WorkId;
        var collectionId = row.CollectionId;

        return entityType switch
        {
            DetailEntityType.TvShow when !string.IsNullOrWhiteSpace(collectionId) => $"/watch/tv/show/{collectionId}",
            DetailEntityType.Movie when !string.IsNullOrWhiteSpace(collectionId) => $"/watch/movie/{workId}?collectionId={collectionId}",
            DetailEntityType.Movie => $"/watch/movie/{workId}",
            DetailEntityType.MusicTrack => $"/details/musictrack/{workId}?context=listen",
            DetailEntityType.Audiobook => $"/details/audiobook/{workId}?context=listen",
            DetailEntityType.ComicIssue => $"/book/{workId}?mode=read",
            _ => $"/book/{workId}?mode=read",
        };
    }

    private static string BuildRecommendationReason(RecommendationWorkRow row)
    {
        var parts = new List<string>();
        if (row.MatchedPeople > 0)
        {
            parts.Add(row.MatchedPeople == 1 ? "shared person" : "shared people");
        }

        if (row.MatchedGenres > 0)
        {
            parts.Add(row.MatchedGenres == 1 ? "shared genre" : "shared genres");
        }

        if (row.MatchedAi > 0)
        {
            parts.Add("similar mood and themes");
        }

        if (row.YearScore >= 1.25)
        {
            parts.Add("nearby year");
        }

        return parts.Count == 0
            ? "Similar library item"
            : string.Join(", ", parts.Select(part => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(part)));
    }

    private static IReadOnlyList<MetadataPill> BuildRecommendationMetadata(RecommendationWorkRow row)
    {
        var pills = new List<MetadataPill>();
        if (!string.IsNullOrWhiteSpace(row.Year))
        {
            pills.Add(new MetadataPill { Label = row.Year!, Kind = "year" });
        }

        var mediaType = InferRecommendationEntityType(row.MediaType);
        pills.Add(new MetadataPill { Label = FormatEntityType(mediaType), Kind = "media_type" });
        return pills;
    }

    private static string? StringValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return bytes.Length == 16
                ? GuidSql.FromDb(bytes).ToString("D")
                : Encoding.UTF8.GetString(bytes);
        }

        var text = Convert.ToString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? ResolveCollectionArtworkUrl(string? value, string? assetIdValue, string kind, string? state)
    {
        if (!Guid.TryParse(assetIdValue, out var assetId))
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return DisplayArtworkUrlResolver.Resolve(value, assetId, kind, state);
    }

    private static int? IntValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            int i => i,
            long l => checked((int)l),
            _ => int.TryParse(Convert.ToString(value), out var parsed) ? parsed : null,
        };
    }

    private static double? DoubleValue(object? value)
    {
        if (value is null or DBNull)
        {
            return null;
        }

        return value switch
        {
            double d => d,
            float f => f,
            decimal m => (double)m,
            int i => i,
            long l => l,
            _ => double.TryParse(Convert.ToString(value), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
        };
    }

    private static string FormatEntityType(DetailEntityType entityType) => entityType switch
    {
        DetailEntityType.TvShow => "TV Show",
        DetailEntityType.MusicTrack => "Music",
        DetailEntityType.ComicIssue => "Comic",
        _ => entityType.ToString(),
    };

    private sealed class RecommendationWorkRow
    {
        public string WorkId { get; init; } = string.Empty;
        public string? CollectionId { get; init; }
        public string? MediaType { get; init; }
        public string? AssetId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? ArtworkUrl { get; init; }
        public string? ArtworkState { get; init; }
        public string? Year { get; init; }
        public double SignalScore { get; init; }
        public int MatchedPeople { get; init; }
        public int MatchedGenres { get; init; }
        public int MatchedAi { get; init; }
        public double YearScore { get; init; }
        public double MediaTypeScore { get; init; }
        public string? MatchedSignals { get; init; }
        public double Score { get; init; }
    }
}
