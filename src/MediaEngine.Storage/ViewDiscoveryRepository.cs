using System.Data;
using Dapper;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// Reads only resolver-authorized, active personal media. Places come from actual
/// local metadata. People come only from named annotations or reviewed identity
/// annotations and retain their provenance in the projection.
/// </summary>
public sealed class ViewDiscoveryRepository(IDatabaseConnection database) : IViewDiscoveryRepository
{
    private const int MaximumLimit = 100;

    private sealed class PlaceRow
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public long AssetCount { get; init; }
        public Guid RepresentativeAssetId { get; init; }
    }

    private sealed class PersonRow
    {
        public string Key { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public long AssetCount { get; init; }
        public Guid RepresentativeAssetId { get; init; }
        public string? AnnotationKinds { get; init; }
        public string? ProvenanceSources { get; init; }
        public long HasReviewedEvidence { get; init; }
    }

    public ViewPlaceDiscoveryPage QueryPlaces(
        ViewPlaceDiscoveryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var libraries = Validate(query.AuthorizedLibraryIds, query.Limit, query.Cursor, query.Search);
        if (libraries.Length == 0)
            return new ViewPlaceDiscoveryPage([], null, false, false);

        ct.ThrowIfCancellationRequested();
        var parameters = Parameters(libraries, query.Limit, query.Search, query.Cursor);
        var libraryPredicate = LibraryPredicate("li", libraries.Length);
        using var connection = database.CreateConnection();
        var hasEligibleData = connection.QuerySingle<bool>(new CommandDefinition($$"""
            SELECT EXISTS (
                SELECT 1
                  FROM local_items li
                  JOIN local_item_metadata lm ON lm.item_id = li.id
                 WHERE ({{libraryPredicate}})
                   AND li.hidden = 0
                   AND li.archived_at IS NULL
                   AND li.trashed_at IS NULL
                   AND lm.latitude IS NOT NULL
                   AND lm.longitude IS NOT NULL);
            """, parameters, cancellationToken: ct));

        var rows = connection.Query<PlaceRow>(new CommandDefinition($$"""
            WITH eligible AS (
                SELECT li.id AS ItemId,
                       COALESCE(li.captured_at, li.created_at) AS EffectiveAt,
                       LOWER(COALESCE(
                           NULLIF(TRIM(lm.location_name), ''),
                           'location')) || '@' ||
                           printf('%.1f,%.1f', ROUND(lm.latitude, 1), ROUND(lm.longitude, 1)) AS PlaceKey,
                       COALESCE(
                           NULLIF(TRIM(lm.location_name), ''),
                           printf('%.3f, %.3f', ROUND(lm.latitude, 3), ROUND(lm.longitude, 3))) AS PlaceName,
                       ROUND(lm.latitude, 3) AS Latitude,
                       ROUND(lm.longitude, 3) AS Longitude
                  FROM local_items li
                  JOIN local_item_metadata lm ON lm.item_id = li.id
                 WHERE ({{libraryPredicate}})
                   AND li.hidden = 0
                   AND li.archived_at IS NULL
                   AND li.trashed_at IS NULL
                   AND lm.latitude IS NOT NULL
                   AND lm.longitude IS NOT NULL
                   AND (@SearchPattern IS NULL
                        OR lm.location_name LIKE @SearchPattern ESCAPE '\'
                        OR printf('%.3f,%.3f', ROUND(lm.latitude, 3), ROUND(lm.longitude, 3))
                           LIKE @SearchPattern ESCAPE '\')
            ), ranked AS (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY PlaceKey
                    ORDER BY EffectiveAt DESC, ItemId DESC) AS RepresentativeRank
                  FROM eligible
            ), grouped AS (
                SELECT PlaceKey AS Key,
                       MAX(PlaceName) AS Name,
                       ROUND(AVG(Latitude), 3) AS Latitude,
                       ROUND(AVG(Longitude), 3) AS Longitude,
                       COUNT(DISTINCT ItemId) AS AssetCount,
                       MAX(CASE WHEN RepresentativeRank = 1 THEN ItemId END) AS RepresentativeAssetId
                  FROM ranked
                 GROUP BY PlaceKey
            )
            SELECT Key, Name, Latitude, Longitude, AssetCount, RepresentativeAssetId
              FROM grouped
             WHERE (@CursorCount IS NULL
                    OR AssetCount < @CursorCount
                    OR (AssetCount = @CursorCount AND Key > @CursorKey))
             ORDER BY AssetCount DESC, Key
             LIMIT @Take;
            """, parameters, cancellationToken: ct)).ToList();

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(row => new ViewPlaceDiscoveryRow(
            row.Key,
            row.Name,
            row.Latitude,
            row.Longitude,
            checked((int)row.AssetCount),
            row.RepresentativeAssetId)).ToList();
        var last = items.LastOrDefault();
        return new ViewPlaceDiscoveryPage(
            items,
            hasMore && last is not null ? new ViewDiscoveryCursor(last.AssetCount, last.Key) : null,
            hasMore,
            hasEligibleData);
    }

    public ViewPeopleDiscoveryPage QueryPeople(
        ViewPeopleDiscoveryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var libraries = Validate(query.AuthorizedLibraryIds, query.Limit, query.Cursor, query.Search);
        if (libraries.Length == 0)
            return new ViewPeopleDiscoveryPage([], null, false, false);

        ct.ThrowIfCancellationRequested();
        var parameters = Parameters(libraries, query.Limit, query.Search, query.Cursor);
        var libraryPredicate = LibraryPredicate("li", libraries.Length);
        var evidencePredicate = """
            ((lia.annotation_kind IN ('person_name', 'named_person', 'face_name'))
             OR (lia.annotation_kind IN ('person_identity', 'face_identity')
                 AND lia.reviewed_at IS NOT NULL))
            """;
        using var connection = database.CreateConnection();
        var hasEligibleData = connection.QuerySingle<bool>(new CommandDefinition($$"""
            SELECT EXISTS (
                SELECT 1
                  FROM local_item_annotations lia
                  JOIN local_items li ON li.id = lia.item_id
                 WHERE ({{libraryPredicate}})
                   AND li.hidden = 0
                   AND li.archived_at IS NULL
                   AND li.trashed_at IS NULL
                   AND TRIM(lia.annotation_value) <> ''
                   AND {{evidencePredicate}});
            """, parameters, cancellationToken: ct));

        var rows = connection.Query<PersonRow>(new CommandDefinition($$"""
            WITH eligible AS (
                SELECT li.id AS ItemId,
                       COALESCE(li.captured_at, li.created_at) AS EffectiveAt,
                       LOWER(TRIM(lia.annotation_value)) AS PersonKey,
                       TRIM(lia.annotation_value) AS DisplayName,
                       lia.annotation_kind AS AnnotationKind,
                       lia.source AS ProvenanceSource,
                       CASE WHEN lia.reviewed_at IS NULL THEN 0 ELSE 1 END AS IsReviewed
                  FROM local_item_annotations lia
                  JOIN local_items li ON li.id = lia.item_id
                 WHERE ({{libraryPredicate}})
                   AND li.hidden = 0
                   AND li.archived_at IS NULL
                   AND li.trashed_at IS NULL
                   AND TRIM(lia.annotation_value) <> ''
                   AND {{evidencePredicate}}
                   AND (@SearchPattern IS NULL
                        OR lia.annotation_value LIKE @SearchPattern ESCAPE '\')
            ), ranked AS (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY PersonKey
                    ORDER BY EffectiveAt DESC, ItemId DESC) AS RepresentativeRank
                  FROM eligible
            ), grouped AS (
                SELECT PersonKey AS Key,
                       MAX(DisplayName) AS DisplayName,
                       COUNT(DISTINCT ItemId) AS AssetCount,
                       MAX(CASE WHEN RepresentativeRank = 1 THEN ItemId END) AS RepresentativeAssetId,
                       GROUP_CONCAT(DISTINCT AnnotationKind) AS AnnotationKinds,
                       GROUP_CONCAT(DISTINCT ProvenanceSource) AS ProvenanceSources,
                       MAX(IsReviewed) AS HasReviewedEvidence
                  FROM ranked
                 GROUP BY PersonKey
            )
            SELECT Key, DisplayName, AssetCount, RepresentativeAssetId,
                   AnnotationKinds, ProvenanceSources, HasReviewedEvidence
              FROM grouped
             WHERE (@CursorCount IS NULL
                    OR AssetCount < @CursorCount
                    OR (AssetCount = @CursorCount AND Key > @CursorKey))
             ORDER BY AssetCount DESC, Key
             LIMIT @Take;
            """, parameters, cancellationToken: ct)).ToList();

        var hasMore = rows.Count > query.Limit;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(row => new ViewPersonDiscoveryRow(
            row.Key,
            row.DisplayName,
            checked((int)row.AssetCount),
            row.RepresentativeAssetId,
            SplitEvidence(row.AnnotationKinds),
            SplitEvidence(row.ProvenanceSources),
            row.HasReviewedEvidence != 0)).ToList();
        var last = items.LastOrDefault();
        return new ViewPeopleDiscoveryPage(
            items,
            hasMore && last is not null ? new ViewDiscoveryCursor(last.AssetCount, last.Key) : null,
            hasMore,
            hasEligibleData);
    }

    private static Guid[] Validate(
        IReadOnlyCollection<Guid> authorizedLibraryIds,
        int limit,
        ViewDiscoveryCursor? cursor,
        string? search)
    {
        ArgumentNullException.ThrowIfNull(authorizedLibraryIds);
        if (limit is < 1 or > MaximumLimit)
            throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {MaximumLimit}.");
        if (authorizedLibraryIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("Authorized library IDs cannot be empty.", nameof(authorizedLibraryIds));
        if (cursor is { AssetCount: < 1 } || cursor is { Key.Length: 0 })
            throw new ArgumentException("The discovery cursor is invalid.", nameof(cursor));
        if (search?.Length > 200)
            throw new ArgumentOutOfRangeException(nameof(search), "Search cannot exceed 200 characters.");
        return authorizedLibraryIds.Distinct().ToArray();
    }

    private static DynamicParameters Parameters(
        IReadOnlyList<Guid> libraries,
        int limit,
        string? search,
        ViewDiscoveryCursor? cursor)
    {
        var parameters = new DynamicParameters(new
        {
            Take = limit + 1,
            SearchPattern = SearchPattern(search),
            CursorCount = cursor?.AssetCount,
            CursorKey = cursor?.Key,
        });
        for (var index = 0; index < libraries.Count; index++)
            parameters.Add($"LibraryId{index}", GuidSql.ToBlob(libraries[index]), DbType.Binary);
        return parameters;
    }

    private static string LibraryPredicate(string alias, int count) =>
        string.Join(" OR ", Enumerable.Range(0, count).Select(index => $"{alias}.library_id = @LibraryId{index}"));

    private static string? SearchPattern(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var escaped = value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static IReadOnlyList<string> SplitEvidence(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
}
