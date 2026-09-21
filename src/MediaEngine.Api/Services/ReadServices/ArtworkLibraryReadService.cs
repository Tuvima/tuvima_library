using Dapper;
using MediaEngine.Api.Endpoints;
using MediaEngine.Contracts.Artwork;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Services.ReadServices;

public sealed class ArtworkLibraryReadService(IDatabaseConnection database)
{
    private static readonly HashSet<string> SupportedArtworkTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "CoverArt", "Headshot", "Banner", "Logo", "NetworkLogo", "StudioLogo",
        "Background", "SeasonPoster", "SeasonThumb", "EpisodeStill", "CharacterPortrait",
    };

    public Task<ArtworkLibraryPageDto> BrowseAsync(
        string? entityKind,
        string? artworkType,
        string? search,
        int offset,
        int limit,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedKind = entityKind?.Trim().ToLowerInvariant() switch
        {
            "media" or "work" => "work",
            "people" or "person" => "person",
            _ => null,
        };
        var normalizedArtworkType = SupportedArtworkTypes.Contains(artworkType?.Trim() ?? string.Empty)
            ? artworkType!.Trim()
            : null;
        var normalizedSearch = string.IsNullOrWhiteSpace(search)
            ? null
            : $"%{EscapeLike(search.Trim())}%";
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);

        using var connection = database.CreateConnection();
        var rows = connection.Query<ArtworkLibraryRow>(new CommandDefinition(Sql, new
        {
            EntityKind = normalizedKind,
            ArtworkType = normalizedArtworkType,
            Search = normalizedSearch,
            Offset = offset,
            Limit = limit,
        }, cancellationToken: ct)).AsList();

        var items = rows.Select(row => new ArtworkLibraryItemDto(
            row.EntityId,
            row.EntityType,
            string.IsNullOrWhiteSpace(row.DisplayTitle) ? "Untitled" : row.DisplayTitle,
            row.MediaType,
            row.Year,
            row.Subtitle,
            BuildImageUrl(row),
            row.PrimaryAssetType,
            Split(row.AssetTypesCsv),
            row.VariantCount,
            row.OwnedWorkCount,
            row.UsesLegacyPersonImage)).ToList();

        return Task.FromResult(new ArtworkLibraryPageDto(
            items,
            offset,
            limit,
            rows.FirstOrDefault()?.TotalCount ?? 0));
    }

    private static string? BuildImageUrl(ArtworkLibraryRow row)
    {
        if (row.PreferredAssetId.HasValue)
        {
            return $"/stream/artwork/{row.PreferredAssetId.Value:D}?size=m";
        }

        return row.UsesLegacyPersonImage
            ? ApiImageUrls.BuildPersonHeadshotUrl(row.EntityId, row.LocalHeadshotPath, row.RemoteHeadshotUrl)
            : null;
    }

    private static IReadOnlyList<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private const string Sql = """
        WITH work_items AS (
            SELECT
                w.id AS EntityId,
                'Work' AS EntityType,
                COALESCE(
                    (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('title','episode_title','issue_title','album','show_name','series','book_title')
                     ORDER BY CASE key WHEN 'title' THEN 0 WHEN 'episode_title' THEN 1 WHEN 'issue_title' THEN 2 ELSE 3 END LIMIT 1),
                    NULLIF(w.parent_key, ''),
                    'Untitled') AS DisplayTitle,
                w.media_type AS MediaType,
                (SELECT value FROM canonical_values WHERE entity_id = w.id AND key IN ('year','release_year','publication_year')
                 ORDER BY CASE key WHEN 'year' THEN 0 ELSE 1 END LIMIT 1) AS Year,
                NULL AS Subtitle,
                (SELECT preferred.id
                   FROM entity_assets preferred
                  WHERE preferred.entity_id = w.id
                    AND preferred.entity_type = 'Work'
                    AND (@ArtworkType IS NULL OR preferred.asset_type = @ArtworkType)
                  ORDER BY preferred.is_preferred DESC,
                           CASE preferred.asset_type
                               WHEN 'CoverArt' THEN 0 WHEN 'Headshot' THEN 1 WHEN 'Background' THEN 2
                               WHEN 'Logo' THEN 3 ELSE 4 END,
                           COALESCE(preferred.updated_at, preferred.created_at) DESC
                  LIMIT 1) AS PreferredAssetId,
                (SELECT preferred.asset_type
                   FROM entity_assets preferred
                  WHERE preferred.entity_id = w.id
                    AND preferred.entity_type = 'Work'
                    AND (@ArtworkType IS NULL OR preferred.asset_type = @ArtworkType)
                  ORDER BY preferred.is_preferred DESC,
                           CASE preferred.asset_type
                               WHEN 'CoverArt' THEN 0 WHEN 'Headshot' THEN 1 WHEN 'Background' THEN 2
                               WHEN 'Logo' THEN 3 ELSE 4 END,
                           COALESCE(preferred.updated_at, preferred.created_at) DESC
                  LIMIT 1) AS PrimaryAssetType,
                GROUP_CONCAT(DISTINCT ea.asset_type) AS AssetTypesCsv,
                COUNT(ea.id) AS VariantCount,
                1 AS OwnedWorkCount,
                0 AS UsesLegacyPersonImage,
                NULL AS LocalHeadshotPath,
                NULL AS RemoteHeadshotUrl
            FROM works w
            LEFT JOIN entity_assets ea
                   ON ea.entity_id = w.id
                  AND ea.entity_type = 'Work'
                  AND (@ArtworkType IS NULL OR ea.asset_type = @ArtworkType)
            WHERE w.ownership = 'Owned'
              AND w.is_catalog_only = 0
            GROUP BY w.id, w.media_type, w.parent_key
            HAVING @ArtworkType IS NULL OR COUNT(ea.id) > 0
        ),
        person_items AS (
            SELECT
                p.id AS EntityId,
                'Person' AS EntityType,
                p.name AS DisplayTitle,
                NULL AS MediaType,
                NULL AS Year,
                (SELECT GROUP_CONCAT(role, ', ')
                   FROM (SELECT role FROM person_roles WHERE person_id = p.id ORDER BY role)) AS Subtitle,
                (SELECT preferred.id
                   FROM entity_assets preferred
                  WHERE preferred.entity_id = p.id
                    AND preferred.entity_type = 'Person'
                    AND (@ArtworkType IS NULL OR preferred.asset_type = @ArtworkType)
                  ORDER BY preferred.is_preferred DESC,
                           CASE preferred.asset_type WHEN 'Headshot' THEN 0 WHEN 'Background' THEN 1 WHEN 'Logo' THEN 2 ELSE 3 END,
                           COALESCE(preferred.updated_at, preferred.created_at) DESC
                  LIMIT 1) AS PreferredAssetId,
                COALESCE(
                    (SELECT preferred.asset_type
                       FROM entity_assets preferred
                      WHERE preferred.entity_id = p.id
                        AND preferred.entity_type = 'Person'
                        AND (@ArtworkType IS NULL OR preferred.asset_type = @ArtworkType)
                      ORDER BY preferred.is_preferred DESC,
                               CASE preferred.asset_type WHEN 'Headshot' THEN 0 WHEN 'Background' THEN 1 WHEN 'Logo' THEN 2 ELSE 3 END,
                               COALESCE(preferred.updated_at, preferred.created_at) DESC
                      LIMIT 1),
                    CASE WHEN (@ArtworkType IS NULL OR @ArtworkType = 'Headshot')
                                   AND (NULLIF(p.local_headshot_path, '') IS NOT NULL OR NULLIF(p.headshot_url, '') IS NOT NULL)
                              THEN 'Headshot' END) AS PrimaryAssetType,
                CASE
                    WHEN COUNT(ea.id) > 0 THEN GROUP_CONCAT(DISTINCT ea.asset_type)
                    WHEN (@ArtworkType IS NULL OR @ArtworkType = 'Headshot')
                         AND (NULLIF(p.local_headshot_path, '') IS NOT NULL OR NULLIF(p.headshot_url, '') IS NOT NULL)
                    THEN 'Headshot'
                END AS AssetTypesCsv,
                COUNT(ea.id) AS VariantCount,
                (SELECT COUNT(DISTINCT editions.work_id)
                   FROM person_media_links links
                   INNER JOIN media_assets assets ON assets.id = links.media_asset_id
                   INNER JOIN editions ON editions.id = assets.edition_id
                  WHERE links.person_id = p.id) AS OwnedWorkCount,
                CASE WHEN COUNT(ea.id) = 0
                           AND (@ArtworkType IS NULL OR @ArtworkType = 'Headshot')
                           AND (NULLIF(p.local_headshot_path, '') IS NOT NULL OR NULLIF(p.headshot_url, '') IS NOT NULL)
                     THEN 1 ELSE 0 END AS UsesLegacyPersonImage,
                p.local_headshot_path AS LocalHeadshotPath,
                p.headshot_url AS RemoteHeadshotUrl
            FROM persons p
            LEFT JOIN entity_assets ea
                   ON ea.entity_id = p.id
                  AND ea.entity_type = 'Person'
                  AND (@ArtworkType IS NULL OR ea.asset_type = @ArtworkType)
            WHERE @ArtworkType IS NULL
               OR @ArtworkType = 'Headshot'
               OR ea.id IS NOT NULL
            GROUP BY p.id, p.name, p.local_headshot_path, p.headshot_url
        ),
        library_items AS (
            SELECT * FROM work_items
            UNION ALL
            SELECT * FROM person_items
        ),
        filtered AS (
            SELECT *
              FROM library_items
             WHERE (@EntityKind IS NULL OR lower(EntityType) = @EntityKind)
               AND (@Search IS NULL OR DisplayTitle LIKE @Search ESCAPE '\')
        )
        SELECT *, COUNT(*) OVER() AS TotalCount
          FROM filtered
         ORDER BY CASE EntityType WHEN 'Person' THEN 1 ELSE 0 END,
                  DisplayTitle COLLATE NOCASE,
                  EntityId
         LIMIT @Limit OFFSET @Offset;
        """;

    private sealed class ArtworkLibraryRow
    {
        public Guid EntityId { get; init; }
        public string EntityType { get; init; } = string.Empty;
        public string DisplayTitle { get; init; } = string.Empty;
        public string? MediaType { get; init; }
        public string? Year { get; init; }
        public string? Subtitle { get; init; }
        public Guid? PreferredAssetId { get; init; }
        public string? PrimaryAssetType { get; init; }
        public string? AssetTypesCsv { get; init; }
        public int VariantCount { get; init; }
        public int OwnedWorkCount { get; init; }
        public bool UsesLegacyPersonImage { get; init; }
        public string? LocalHeadshotPath { get; init; }
        public string? RemoteHeadshotUrl { get; init; }
        public int TotalCount { get; init; }
    }
}
