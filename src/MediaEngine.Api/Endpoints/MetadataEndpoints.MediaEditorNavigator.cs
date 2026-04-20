using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using Dapper;
using MediaEngine.Api.Security;
using MediaEngine.Storage.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Api.Endpoints;

public static partial class MetadataEndpoints
{
    private static void MapMediaEditorNavigatorEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{entityId:guid}/navigator", async (
            Guid entityId,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var navigator = await BuildMediaEditorNavigatorAsync(entityId, db, ct);
            return navigator is null
                ? Results.NotFound($"Navigator for {entityId} not found.")
                : Results.Ok(navigator);
        })
        .WithName("GetMediaEditorNavigator")
        .WithSummary("Resolve series-aware editor navigation for a launch entity.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapGet("/{entityId:guid}/membership-suggestions", async (
            Guid entityId,
            string field,
            string? query,
            Guid? parentEntityId,
            string? parentValue,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var suggestions = await BuildMembershipSuggestionsAsync(entityId, field, query, parentEntityId, parentValue, db, ct);
            return Results.Ok(suggestions);
        })
        .WithName("GetMediaEditorMembershipSuggestions")
        .WithSummary("Return same-media-type autocomplete targets for membership correction.")
        .Produces(StatusCodes.Status200OK)
        .RequireAnyRole();

        group.MapPost("/{entityId:guid}/membership-preview", async (
            Guid entityId,
            MembershipPreviewRequest request,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var preview = await BuildMembershipPreviewAsync(entityId, request, db, ct);
            return preview is null
                ? Results.NotFound($"Membership preview for {entityId} not found.")
                : Results.Ok(preview);
        })
        .WithName("PreviewMediaEditorMembershipChange")
        .WithSummary("Preview a hierarchy move or parent identity rename before applying it.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();

        group.MapPost("/{entityId:guid}/membership-apply", async (
            Guid entityId,
            MembershipPreviewRequest request,
            IDatabaseConnection db,
            CancellationToken ct) =>
        {
            var result = await ApplyMembershipChangeAsync(entityId, request, db, ct);
            return result is null
                ? Results.NotFound($"Membership apply for {entityId} not found.")
                : Results.Ok(result);
        })
        .WithName("ApplyMediaEditorMembershipChange")
        .WithSummary("Apply a confirmed hierarchy move or parent identity rename.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAnyRole();
    }

    private static async Task<MediaEditorNavigatorEnvelope?> BuildMediaEditorNavigatorAsync(
        Guid entityId,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var launch = await ResolveEditorLaunchContextAsync(entityId, db, ct);
        if (launch is null)
            return null;

        var mediaType = NormalizeEditorMediaType(launch.MediaType);
        var rootWorkId = launch.RootWorkId == Guid.Empty ? launch.WorkId : launch.RootWorkId;

        using var conn = db.CreateConnection();
        var treeRows = QueryNavigatorTree(conn, rootWorkId);
        if (treeRows.Count == 0)
            return null;

        var enabled = IsSeriesAwareNavigatorEnabled(mediaType, launch, treeRows);
        if (!enabled)
        {
            return new MediaEditorNavigatorEnvelope(
                Enabled: false,
                MediaType: mediaType,
                ContainerEntityId: launch.WorkId,
                SelectedEntityId: launch.WorkId,
                ContainerLabel: GetNavigatorContainerLabel(mediaType),
                ContainerTitle: string.Empty,
                ContainerSubtitle: null,
                Nodes: []);
        }

        var valueMap = QueryNavigatorValues(conn, treeRows);
        var descendantOwnedCounts = ComputeNavigatorOwnedCounts(treeRows);
        var orderedRows = OrderNavigatorRows(treeRows, valueMap);
        var nodes = orderedRows
            .Select(row => BuildNavigatorNodeEnvelope(mediaType, row, valueMap, descendantOwnedCounts))
            .ToList();

        var rootNode = nodes.FirstOrDefault(node => node.IsRoot)
            ?? nodes.FirstOrDefault();

        return new MediaEditorNavigatorEnvelope(
            Enabled: true,
            MediaType: mediaType,
            ContainerEntityId: rootWorkId,
            SelectedEntityId: launch.WorkId,
            ContainerLabel: GetNavigatorContainerLabel(mediaType),
            ContainerTitle: rootNode?.Title ?? string.Empty,
            ContainerSubtitle: rootNode?.Subtitle,
            Nodes: nodes);
    }

    private static async Task<IReadOnlyList<MembershipSuggestionEnvelope>> BuildMembershipSuggestionsAsync(
        Guid entityId,
        string field,
        string? query,
        Guid? parentEntityId,
        string? parentValue,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var launch = await ResolveEditorLaunchContextAsync(entityId, db, ct);
        if (launch is null)
            return [];

        var mediaType = NormalizeEditorMediaType(launch.MediaType);
        var normalizedField = (field ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedQuery = (query ?? string.Empty).Trim();

        using var conn = db.CreateConnection();
        return normalizedField switch
        {
            "show" when mediaType == "TV" => QueryParentSuggestions(
                conn,
                mediaType,
                normalizedQuery,
                suggestionKind: "show",
                titleSelector: row => FirstNonBlank(row.WorkShowName, row.WorkTitle, FormatParentKeyFallback(row.ParentKey)),
                subtitleSelector: row => FirstNonBlank(row.WorkYear, row.WorkNetwork)),

            "season" when mediaType == "TV" && parentEntityId.HasValue => QuerySeasonSuggestions(conn, parentEntityId.Value, normalizedQuery),

            "artist" when mediaType == "Music" => QueryDistinctTextSuggestions(
                conn,
                mediaType,
                normalizedQuery,
                titleKey: "artist",
                subtitleKeys: ["album", "year"],
                suggestionKind: "artist"),

            "album" when mediaType == "Music" => QueryParentSuggestions(
                conn,
                mediaType,
                normalizedQuery,
                suggestionKind: "album",
                titleSelector: row => FirstNonBlank(row.WorkAlbum, row.WorkTitle, FormatParentKeyFallback(row.ParentKey)),
                subtitleSelector: row => BuildDelimitedLabel(FirstNonBlank(row.WorkArtist), FirstNonBlank(row.WorkYear)),
                additionalFilter: row => string.IsNullOrWhiteSpace(parentValue)
                    || (!string.IsNullOrWhiteSpace(row.WorkArtist)
                        && row.WorkArtist.Contains(parentValue, StringComparison.OrdinalIgnoreCase))),

            "series" when mediaType is "Comics" or "Books" or "Audiobooks" => QueryParentSuggestions(
                conn,
                mediaType,
                normalizedQuery,
                suggestionKind: "series",
                titleSelector: row => FirstNonBlank(row.WorkSeries, row.WorkTitle, FormatParentKeyFallback(row.ParentKey)),
                subtitleSelector: row => BuildDelimitedLabel(FirstNonBlank(row.WorkAuthor), FirstNonBlank(row.WorkYear)),
                additionalFilter: row => mediaType is not ("Books" or "Audiobooks")
                    || string.IsNullOrWhiteSpace(parentValue)
                    || (!string.IsNullOrWhiteSpace(row.WorkAuthor)
                        && row.WorkAuthor.Contains(parentValue, StringComparison.OrdinalIgnoreCase))),

            "author" when mediaType is "Books" or "Audiobooks" => QueryDistinctTextSuggestions(
                conn,
                mediaType,
                normalizedQuery,
                titleKey: "author",
                subtitleKeys: ["series", "year"],
                suggestionKind: "author"),

            _ => [],
        };
    }

    private static async Task<MembershipPreviewEnvelope?> BuildMembershipPreviewAsync(
        Guid entityId,
        MembershipPreviewRequest request,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var launch = await ResolveEditorLaunchContextAsync(entityId, db, ct);
        if (launch is null)
            return null;

        using var conn = db.CreateConnection();
        var entityRow = conn.QueryFirstOrDefault<MembershipEntityRow>("""
            SELECT w.id             AS WorkId,
                   w.media_type     AS MediaType,
                   w.work_kind      AS WorkKind,
                   w.parent_work_id AS ParentWorkId,
                   w.ordinal        AS Ordinal,
                   w.parent_key     AS ParentKey,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        if (entityRow is null)
            return null;

        var plan = ResolveMembershipPlan(entityRow, request);
        return await FinalizeMembershipPreviewAsync(conn, plan, request, ct);
    }

    private static async Task<MembershipPreviewEnvelope?> ApplyMembershipChangeAsync(
        Guid entityId,
        MembershipPreviewRequest request,
        IDatabaseConnection db,
        CancellationToken ct)
    {
        var preview = await BuildMembershipPreviewAsync(entityId, request, db, ct);
        if (preview is null)
            return null;

        if (!preview.CanApply || string.Equals(preview.Action, "none", StringComparison.OrdinalIgnoreCase))
            return preview with { Applied = false };

        await db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = db.CreateConnection();
            using var tx = conn.BeginTransaction();

            var entityRow = conn.QueryFirstOrDefault<MembershipEntityRow>("""
                SELECT w.id             AS WorkId,
                       w.media_type     AS MediaType,
                       w.work_kind      AS WorkKind,
                       w.parent_work_id AS ParentWorkId,
                       w.ordinal        AS Ordinal,
                       w.parent_key     AS ParentKey,
                       COALESCE(gp.id, p.id, w.id) AS RootWorkId
                FROM works w
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                WHERE w.id = @entityId
                LIMIT 1;
                """, new { entityId = entityId.ToString() }, tx);

            if (entityRow is null)
            {
                tx.Rollback();
                return null;
            }

            var plan = ResolveMembershipPlan(entityRow, request);
            var finalized = await FinalizeMembershipPlanAsync(conn, tx, plan, request, applyChanges: true, ct);
            tx.Commit();
            return finalized with { Applied = true };
        }
        finally
        {
            db.ReleaseWriteLock();
        }
    }

    private static IReadOnlyList<NavigatorTreeRow> QueryNavigatorTree(SqliteConnection conn, Guid rootWorkId) =>
        conn.Query<NavigatorTreeRow>("""
            WITH RECURSIVE work_tree(id, parent_work_id, depth) AS (
                SELECT w.id, w.parent_work_id, 0
                FROM works w
                WHERE w.id = @rootId
                UNION ALL
                SELECT child.id, child.parent_work_id, work_tree.depth + 1
                FROM works child
                INNER JOIN work_tree ON child.parent_work_id = work_tree.id
            )
            SELECT work_tree.id             AS WorkId,
                   work_tree.parent_work_id AS ParentWorkId,
                   work_tree.depth          AS Depth,
                   w.work_kind              AS WorkKind,
                   w.ordinal                AS Ordinal,
                   w.is_catalog_only        AS IsCatalogOnly,
                   w.parent_key             AS ParentKey
            FROM work_tree
            INNER JOIN works w ON w.id = work_tree.id
            ORDER BY work_tree.depth,
                     COALESCE(w.parent_work_id, ''),
                     COALESCE(w.ordinal, 2147483647),
                     w.id;
            """, new { rootId = rootWorkId.ToString() }).ToList();

    private static Dictionary<Guid, NavigatorValueRow> QueryNavigatorValues(
        SqliteConnection conn,
        IReadOnlyList<NavigatorTreeRow> rows)
    {
        var workIds = rows.Select(row => row.WorkId.ToString()).Distinct().ToArray();
        if (workIds.Length == 0)
            return [];

        var result = conn.Query<NavigatorValueRow>("""
            WITH representative_assets AS (
                SELECT e.work_id AS WorkId,
                       MIN(ma.id) AS AssetId
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id IN @workIds
                GROUP BY e.work_id
            )
            SELECT w.id AS WorkId,
                   ra.AssetId,
                   MAX(CASE WHEN wv.key = 'title' THEN wv.value END) AS WorkTitle,
                   MAX(CASE WHEN wv.key = 'show_name' THEN wv.value END) AS WorkShowName,
                   MAX(CASE WHEN wv.key = 'network' THEN wv.value END) AS WorkNetwork,
                   MAX(CASE WHEN wv.key = 'album' THEN wv.value END) AS WorkAlbum,
                   MAX(CASE WHEN wv.key = 'artist' THEN wv.value END) AS WorkArtist,
                   MAX(CASE WHEN wv.key = 'series' THEN wv.value END) AS WorkSeries,
                   MAX(CASE WHEN wv.key = 'author' THEN wv.value END) AS WorkAuthor,
                   MAX(CASE WHEN wv.key = 'year' THEN wv.value END) AS WorkYear,
                   MAX(CASE WHEN wv.key = 'season_number' THEN wv.value END) AS WorkSeasonNumber,
                   MAX(CASE WHEN av.key = 'title' THEN av.value END) AS AssetTitle,
                   MAX(CASE WHEN av.key = 'episode_title' THEN av.value END) AS AssetEpisodeTitle,
                   MAX(CASE WHEN av.key = 'episode_number' THEN av.value END) AS AssetEpisodeNumber,
                   MAX(CASE WHEN av.key = 'season_number' THEN av.value END) AS AssetSeasonNumber,
                   MAX(CASE WHEN av.key = 'track_number' THEN av.value END) AS AssetTrackNumber,
                   MAX(CASE WHEN av.key = 'series_position' THEN av.value END) AS AssetSeriesPosition,
                   MAX(CASE WHEN av.key = 'issue_number' THEN av.value END) AS AssetIssueNumber,
                   MAX(CASE WHEN av.key = 'volume' THEN av.value END) AS AssetVolume,
                   w.parent_key AS ParentKey
            FROM works w
            LEFT JOIN representative_assets ra ON ra.WorkId = w.id
            LEFT JOIN canonical_values wv ON wv.entity_id = w.id
            LEFT JOIN canonical_values av ON av.entity_id = ra.AssetId
            WHERE w.id IN @workIds
            GROUP BY w.id, ra.AssetId, w.parent_key;
            """, new { workIds }).ToList();

        return result.ToDictionary(row => row.WorkId);
    }

    private static bool IsSeriesAwareNavigatorEnabled(
        string mediaType,
        EditorLaunchContext launch,
        IReadOnlyList<NavigatorTreeRow> rows) =>
        mediaType switch
        {
            "TV" or "Music" => true,
            "Comics" or "Books" or "Audiobooks" => string.Equals(launch.WorkKind, "parent", StringComparison.OrdinalIgnoreCase)
                                                   || rows.Any(row => row.Depth > 0),
            _ => false,
        };

    private static IReadOnlyDictionary<Guid, int> ComputeNavigatorOwnedCounts(IReadOnlyList<NavigatorTreeRow> rows)
    {
        var childrenByParent = rows
            .Where(row => row.ParentWorkId.HasValue)
            .GroupBy(row => row.ParentWorkId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(row => row.WorkId).ToList());
        var rowMap = rows.ToDictionary(row => row.WorkId);
        var cache = new Dictionary<Guid, int>();

        int CountOwned(Guid workId)
        {
            if (cache.TryGetValue(workId, out var cached))
                return cached;

            var row = rowMap[workId];
            var isParent = string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase);
            int count;
            if (!isParent)
            {
                count = row.IsCatalogOnly ? 0 : 1;
            }
            else
            {
                count = childrenByParent.TryGetValue(workId, out var children)
                    ? children.Sum(CountOwned)
                    : 0;
            }

            cache[workId] = count;
            return count;
        }

        foreach (var row in rows)
            CountOwned(row.WorkId);

        return cache;
    }

    private static IReadOnlyList<NavigatorTreeRow> OrderNavigatorRows(
        IReadOnlyList<NavigatorTreeRow> rows,
        IReadOnlyDictionary<Guid, NavigatorValueRow> valueMap) =>
        rows.OrderBy(row => row.Depth)
            .ThenBy(row => row.ParentWorkId)
            .ThenBy(row => row.Ordinal ?? int.MaxValue)
            .ThenBy(row =>
            {
                if (!valueMap.TryGetValue(row.WorkId, out var value))
                    return row.WorkId.ToString("D");

                return FirstNonBlank(
                    value.AssetEpisodeTitle,
                    value.AssetTitle,
                    value.WorkTitle,
                    value.WorkShowName,
                    value.WorkAlbum,
                    value.WorkSeries,
                    value.WorkAuthor,
                    row.WorkId.ToString("D"));
            }, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static MediaEditorNavigatorNodeEnvelope BuildNavigatorNodeEnvelope(
        string mediaType,
        NavigatorTreeRow row,
        IReadOnlyDictionary<Guid, NavigatorValueRow> valueMap,
        IReadOnlyDictionary<Guid, int> descendantOwnedCounts)
    {
        valueMap.TryGetValue(row.WorkId, out var value);
        var nodeKind = ResolveNavigatorNodeKind(mediaType, row);
        var scopeId = ResolveNavigatorScopeId(mediaType, nodeKind);
        var title = ResolveNavigatorTitle(mediaType, row, value);
        var subtitle = ResolveNavigatorSubtitle(mediaType, row, value, descendantOwnedCounts.TryGetValue(row.WorkId, out var ownedCount) ? ownedCount : 0);
        var ordinalLabel = ResolveNavigatorOrdinalLabel(mediaType, row, value);
        var isParent = string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase);
        var quarantineCount = descendantOwnedCounts.TryGetValue(row.WorkId, out var count) ? count : 0;
        var isOwned = isParent ? quarantineCount > 0 : !row.IsCatalogOnly;

        return new MediaEditorNavigatorNodeEnvelope(
            NodeId: row.WorkId,
            ParentNodeId: row.ParentWorkId,
            EntityId: row.WorkId,
            ScopeId: scopeId,
            NodeKind: nodeKind,
            Label: GetNavigatorNodeLabel(nodeKind, row, value),
            Title: title,
            Subtitle: subtitle,
            OrdinalLabel: ordinalLabel,
            Depth: row.Depth,
            IsRoot: row.Depth == 0,
            IsLeaf: !isParent,
            IsOwned: isOwned,
            CanQuarantine: quarantineCount > 0,
            QuarantineCount: quarantineCount);
    }

    private static string ResolveNavigatorNodeKind(string mediaType, NavigatorTreeRow row) =>
        mediaType switch
        {
            "TV" when row.Depth == 0 => "series",
            "TV" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => "season",
            "TV" => "episode",
            "Music" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => "album",
            "Music" => "track",
            "Comics" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => "series",
            "Comics" => "issue",
            "Audiobooks" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => "series",
            "Audiobooks" => "audiobook",
            "Books" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => "series",
            "Books" => "book",
            _ => "work",
        };

    private static string ResolveNavigatorScopeId(string mediaType, string nodeKind) =>
        nodeKind switch
        {
            "series" => "series",
            "season" => "season",
            "episode" => "episode",
            "album" => "album",
            "track" => "track",
            "issue" or "book" or "audiobook" when mediaType is "Comics" or "Books" or "Audiobooks" => "volume_issue",
            _ => "work",
        };

    private static string GetNavigatorContainerLabel(string mediaType) =>
        mediaType switch
        {
            "TV" => "Series",
            "Music" => "Album",
            "Comics" or "Books" or "Audiobooks" => "Series",
            _ => "Item",
        };

    private static string GetNavigatorNodeLabel(string nodeKind, NavigatorTreeRow row, NavigatorValueRow? value) =>
        nodeKind switch
        {
            "series" => "Series",
            "season" => ResolveNavigatorOrdinalLabel("TV", row, value) ?? "Season",
            "episode" => ResolveNavigatorOrdinalLabel("TV", row, value) ?? "Episode",
            "album" => "Album",
            "track" => ResolveNavigatorOrdinalLabel("Music", row, value) ?? "Track",
            "issue" => ResolveNavigatorOrdinalLabel("Comics", row, value) ?? "Issue",
            "book" => ResolveNavigatorOrdinalLabel("Books", row, value) ?? "Book",
            "audiobook" => ResolveNavigatorOrdinalLabel("Audiobooks", row, value) ?? "Audiobook",
            _ => "Item",
        };

    private static string ResolveNavigatorTitle(string mediaType, NavigatorTreeRow row, NavigatorValueRow? value) =>
        mediaType switch
        {
            "TV" when row.Depth == 0 => FirstNonBlank(value?.WorkShowName, value?.WorkTitle, FormatParentKeyFallback(row.ParentKey), "Series"),
            "TV" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) =>
                $"Season {ParseNavigatorOrdinal(value?.AssetSeasonNumber ?? value?.WorkSeasonNumber, row.Ordinal)?.ToString(CultureInfo.InvariantCulture) ?? "?"}",
            "TV" => FirstNonBlank(value?.AssetEpisodeTitle, value?.AssetTitle, value?.WorkTitle, $"Episode {row.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? "?"}"),
            "Music" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) =>
                FirstNonBlank(value?.WorkAlbum, value?.WorkTitle, FormatParentKeyFallback(row.ParentKey), "Album"),
            "Music" => FirstNonBlank(value?.AssetTitle, value?.WorkTitle, $"Track {row.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? "?"}"),
            "Comics" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) =>
                FirstNonBlank(value?.WorkSeries, value?.WorkTitle, FormatParentKeyFallback(row.ParentKey), "Series"),
            "Comics" => FirstNonBlank(value?.AssetTitle, value?.WorkTitle, $"Issue {row.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? "?"}"),
            "Books" or "Audiobooks" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) =>
                FirstNonBlank(value?.WorkSeries, value?.WorkTitle, FormatParentKeyFallback(row.ParentKey), "Series"),
            _ => FirstNonBlank(value?.AssetTitle, value?.WorkTitle, "Item"),
        };

    private static string? ResolveNavigatorSubtitle(string mediaType, NavigatorTreeRow row, NavigatorValueRow? value, int ownedCount)
    {
        if (mediaType == "TV" && string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) && row.Depth > 0)
            return ownedCount == 1 ? "1 episode" : $"{ownedCount} episodes";

        if (row.Depth == 0)
        {
            return mediaType switch
            {
                "TV" => BuildDelimitedLabel(FirstNonBlank(value?.WorkYear), FirstNonBlank(value?.WorkNetwork)),
                "Music" => BuildDelimitedLabel(FirstNonBlank(value?.WorkArtist), FirstNonBlank(value?.WorkYear)),
                "Comics" or "Books" or "Audiobooks" => BuildDelimitedLabel(FirstNonBlank(value?.WorkAuthor), FirstNonBlank(value?.WorkYear)),
                _ => FirstNonBlank(value?.WorkYear),
            };
        }

        return mediaType switch
        {
            "Music" => FirstNonBlank(value?.WorkArtist),
            "Books" or "Audiobooks" => FirstNonBlank(value?.WorkAuthor),
            "Comics" => FirstNonBlank(value?.AssetVolume, value?.WorkYear),
            _ => null,
        };
    }

    private static string? ResolveNavigatorOrdinalLabel(string mediaType, NavigatorTreeRow row, NavigatorValueRow? value)
    {
        var ordinal = ParseNavigatorOrdinal(
            mediaType switch
            {
                "TV" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => value?.AssetSeasonNumber ?? value?.WorkSeasonNumber,
                "TV" => value?.AssetEpisodeNumber,
                "Music" => value?.AssetTrackNumber,
                "Comics" => value?.AssetIssueNumber ?? value?.AssetSeriesPosition,
                "Books" or "Audiobooks" => value?.AssetSeriesPosition,
                _ => null,
            },
            row.Ordinal);

        if (ordinal is null)
            return null;

        return mediaType switch
        {
            "TV" when string.Equals(row.WorkKind, "parent", StringComparison.OrdinalIgnoreCase) => $"Season {ordinal}",
            "TV" => $"Episode {ordinal}",
            "Music" => $"Track {ordinal}",
            "Comics" => $"Issue {ordinal}",
            "Books" or "Audiobooks" => $"Book {ordinal}",
            _ => ordinal.Value.ToString(CultureInfo.InvariantCulture),
        };
    }

    private static IReadOnlyList<MembershipSuggestionEnvelope> QueryParentSuggestions(
        SqliteConnection conn,
        string mediaType,
        string query,
        string suggestionKind,
        Func<NavigatorValueRow, string> titleSelector,
        Func<NavigatorValueRow, string?> subtitleSelector,
        Func<NavigatorValueRow, bool>? additionalFilter = null)
    {
        var rows = QueryNavigatorParentValues(conn, mediaType);
        return rows
            .Where(row =>
            {
                var title = titleSelector(row);
                return !string.IsNullOrWhiteSpace(title)
                       && (string.IsNullOrWhiteSpace(query) || title.Contains(query, StringComparison.OrdinalIgnoreCase))
                       && (additionalFilter?.Invoke(row) ?? true);
            })
            .OrderBy(row => titleSelector(row), StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(row => new MembershipSuggestionEnvelope(
                row.WorkId,
                suggestionKind,
                titleSelector(row),
                subtitleSelector(row)))
            .ToList();
    }

    private static IReadOnlyList<MembershipSuggestionEnvelope> QuerySeasonSuggestions(
        SqliteConnection conn,
        Guid showEntityId,
        string query)
    {
        var rows = conn.Query<NavigatorTreeRow>("""
            SELECT w.id             AS WorkId,
                   w.parent_work_id AS ParentWorkId,
                   1                AS Depth,
                   w.work_kind      AS WorkKind,
                   w.ordinal        AS Ordinal,
                   w.is_catalog_only AS IsCatalogOnly,
                   w.parent_key     AS ParentKey
            FROM works w
            WHERE w.parent_work_id = @showId
              AND w.work_kind = 'parent'
            ORDER BY COALESCE(w.ordinal, 2147483647), w.id;
            """, new { showId = showEntityId.ToString() }).ToList();

        return rows
            .Select(row =>
            {
                var label = $"Season {row.Ordinal?.ToString(CultureInfo.InvariantCulture) ?? "?"}";
                return new MembershipSuggestionEnvelope(row.WorkId, "season", label, null);
            })
            .Where(row => string.IsNullOrWhiteSpace(query) || row.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<MembershipSuggestionEnvelope> QueryDistinctTextSuggestions(
        SqliteConnection conn,
        string mediaType,
        string query,
        string titleKey,
        string[] subtitleKeys,
        string suggestionKind)
    {
        var rows = QueryNavigatorParentValues(conn, mediaType);
        IEnumerable<NavigatorValueRow> filtered = rows.Where(row =>
        {
            var value = GetParentSuggestionValue(row, titleKey);
            return !string.IsNullOrWhiteSpace(value)
                   && (string.IsNullOrWhiteSpace(query) || value.Contains(query, StringComparison.OrdinalIgnoreCase));
        });

        return filtered
            .GroupBy(row => GetParentSuggestionValue(row, titleKey)!, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group.First();
                var subtitle = subtitleKeys
                    .Select(keyName => GetParentSuggestionValue(representative, keyName))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                return new MembershipSuggestionEnvelope(representative.WorkId, suggestionKind, group.Key, subtitle);
            })
            .OrderBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    private static IReadOnlyList<NavigatorValueRow> QueryNavigatorParentValues(SqliteConnection conn, string mediaType) =>
        conn.Query<NavigatorValueRow>("""
            SELECT w.id AS WorkId,
                   NULL AS AssetId,
                   MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS WorkTitle,
                   MAX(CASE WHEN cv.key = 'show_name' THEN cv.value END) AS WorkShowName,
                   MAX(CASE WHEN cv.key = 'network' THEN cv.value END) AS WorkNetwork,
                   MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS WorkAlbum,
                   MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS WorkArtist,
                   MAX(CASE WHEN cv.key = 'series' THEN cv.value END) AS WorkSeries,
                   MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS WorkAuthor,
                   MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS WorkYear,
                   MAX(CASE WHEN cv.key = 'season_number' THEN cv.value END) AS WorkSeasonNumber,
                   NULL AS AssetTitle,
                   NULL AS AssetEpisodeTitle,
                   NULL AS AssetEpisodeNumber,
                   NULL AS AssetSeasonNumber,
                   NULL AS AssetTrackNumber,
                   NULL AS AssetSeriesPosition,
                   NULL AS AssetIssueNumber,
                   NULL AS AssetVolume,
                   w.parent_key AS ParentKey
            FROM works w
            LEFT JOIN canonical_values cv ON cv.entity_id = w.id
            WHERE w.media_type = @mediaType
              AND w.work_kind = 'parent'
            GROUP BY w.id, w.parent_key;
            """, new { mediaType }).ToList();

    private static string? GetParentSuggestionValue(NavigatorValueRow row, string key) =>
        key switch
        {
            "show_name" => row.WorkShowName,
            "album" => row.WorkAlbum,
            "artist" => row.WorkArtist,
            "series" => row.WorkSeries,
            "author" => row.WorkAuthor,
            "year" => row.WorkYear,
            "title" => row.WorkTitle,
            _ => null,
        };

    private static MembershipPlan ResolveMembershipPlan(
        MembershipEntityRow entityRow,
        MembershipPreviewRequest request)
    {
        var mediaType = NormalizeEditorMediaType(entityRow.MediaType);
        var fields = request.FieldValues ?? new(StringComparer.OrdinalIgnoreCase);
        var targets = request.SelectedTargetIds ?? new(StringComparer.OrdinalIgnoreCase);
        var workKind = entityRow.WorkKind.Trim().ToLowerInvariant();

        if (mediaType == "TV")
        {
            if (string.Equals(workKind, "child", StringComparison.OrdinalIgnoreCase))
            {
                var showName = FirstNonBlank(GetRequestValue(fields, "show_name"));
                var seasonNumber = ParseNavigatorOrdinal(GetRequestValue(fields, "season_number"), null);
                var episodeNumber = ParseNavigatorOrdinal(GetRequestValue(fields, "episode_number"), entityRow.Ordinal);
                return new MembershipPlan(
                    Action: "move_child",
                    MediaType: mediaType,
                    CurrentEntityId: entityRow.WorkId,
                    CurrentParentEntityId: entityRow.ParentWorkId,
                    CurrentRootEntityId: entityRow.RootWorkId,
                    CurrentOrdinal: entityRow.Ordinal,
                    RequestedTitle: FirstNonBlank(GetRequestValue(fields, "episode_title")),
                    RequestedParentLabel: showName,
                    RequestedSecondaryLabel: seasonNumber?.ToString(CultureInfo.InvariantCulture),
                    RequestedParentKey: BuildHierarchyParentKey(showName),
                    RequestedOrdinal: episodeNumber,
                    SelectedPrimaryTargetId: GetRequestTarget(targets, "show"),
                    SelectedSecondaryTargetId: GetRequestTarget(targets, "season"));
            }

            if (string.Equals(workKind, "parent", StringComparison.OrdinalIgnoreCase) && entityRow.ParentWorkId.HasValue)
            {
                var seasonNumber = ParseNavigatorOrdinal(GetRequestValue(fields, "season_number"), entityRow.Ordinal);
                return new MembershipPlan(
                    Action: "move_season",
                    MediaType: mediaType,
                    CurrentEntityId: entityRow.WorkId,
                    CurrentParentEntityId: entityRow.ParentWorkId,
                    CurrentRootEntityId: entityRow.RootWorkId,
                    CurrentOrdinal: entityRow.Ordinal,
                    RequestedTitle: null,
                    RequestedParentLabel: null,
                    RequestedSecondaryLabel: seasonNumber?.ToString(CultureInfo.InvariantCulture),
                    RequestedParentKey: null,
                    RequestedOrdinal: seasonNumber,
                    SelectedPrimaryTargetId: null,
                    SelectedSecondaryTargetId: null);
            }

            var renamedShowName = FirstNonBlank(GetRequestValue(fields, "show_name"));
            return new MembershipPlan(
                Action: "rename_container",
                MediaType: mediaType,
                CurrentEntityId: entityRow.WorkId,
                CurrentParentEntityId: entityRow.ParentWorkId,
                CurrentRootEntityId: entityRow.RootWorkId,
                CurrentOrdinal: entityRow.Ordinal,
                RequestedTitle: renamedShowName,
                RequestedParentLabel: renamedShowName,
                RequestedSecondaryLabel: null,
                RequestedParentKey: BuildHierarchyParentKey(renamedShowName),
                RequestedOrdinal: entityRow.Ordinal,
                SelectedPrimaryTargetId: null,
                SelectedSecondaryTargetId: null);
        }

        if (mediaType == "Music")
        {
            if (string.Equals(workKind, "child", StringComparison.OrdinalIgnoreCase))
            {
                var artist = FirstNonBlank(GetRequestValue(fields, "artist"), GetRequestValue(fields, "album_artist"));
                var album = FirstNonBlank(GetRequestValue(fields, "album"));
                var trackNumber = ParseNavigatorOrdinal(GetRequestValue(fields, "track_number"), entityRow.Ordinal);
                return new MembershipPlan(
                    Action: "move_child",
                    MediaType: mediaType,
                    CurrentEntityId: entityRow.WorkId,
                    CurrentParentEntityId: entityRow.ParentWorkId,
                    CurrentRootEntityId: entityRow.RootWorkId,
                    CurrentOrdinal: entityRow.Ordinal,
                    RequestedTitle: FirstNonBlank(GetRequestValue(fields, "title")),
                    RequestedParentLabel: album,
                    RequestedSecondaryLabel: artist,
                    RequestedParentKey: BuildHierarchyParentKey(artist, album),
                    RequestedOrdinal: trackNumber,
                    SelectedPrimaryTargetId: GetRequestTarget(targets, "album"),
                    SelectedSecondaryTargetId: null);
            }

            var renamedArtist = FirstNonBlank(GetRequestValue(fields, "artist"), GetRequestValue(fields, "album_artist"));
            var renamedAlbum = FirstNonBlank(GetRequestValue(fields, "album"));
            return new MembershipPlan(
                Action: "rename_container",
                MediaType: mediaType,
                CurrentEntityId: entityRow.WorkId,
                CurrentParentEntityId: entityRow.ParentWorkId,
                CurrentRootEntityId: entityRow.RootWorkId,
                CurrentOrdinal: entityRow.Ordinal,
                RequestedTitle: renamedAlbum,
                RequestedParentLabel: renamedAlbum,
                RequestedSecondaryLabel: renamedArtist,
                RequestedParentKey: BuildHierarchyParentKey(renamedArtist, renamedAlbum),
                RequestedOrdinal: entityRow.Ordinal,
                SelectedPrimaryTargetId: null,
                SelectedSecondaryTargetId: null);
        }

        if (mediaType is "Comics" or "Books" or "Audiobooks")
        {
            if (string.Equals(workKind, "child", StringComparison.OrdinalIgnoreCase))
            {
                var series = FirstNonBlank(GetRequestValue(fields, "series"));
                var author = FirstNonBlank(GetRequestValue(fields, "author"), GetRequestValue(fields, "creator"));
                var ordinal = ParseNavigatorOrdinal(
                    GetRequestValue(fields, mediaType == "Comics" ? "issue_number" : "series_position"),
                    entityRow.Ordinal);
                return new MembershipPlan(
                    Action: "move_child",
                    MediaType: mediaType,
                    CurrentEntityId: entityRow.WorkId,
                    CurrentParentEntityId: entityRow.ParentWorkId,
                    CurrentRootEntityId: entityRow.RootWorkId,
                    CurrentOrdinal: entityRow.Ordinal,
                    RequestedTitle: FirstNonBlank(GetRequestValue(fields, "title")),
                    RequestedParentLabel: series,
                    RequestedSecondaryLabel: author,
                    RequestedParentKey: mediaType == "Comics"
                        ? BuildHierarchyParentKey(series)
                        : BuildHierarchyParentKey(author, series),
                    RequestedOrdinal: ordinal,
                    SelectedPrimaryTargetId: GetRequestTarget(targets, "series"),
                    SelectedSecondaryTargetId: null);
            }

            var renamedSeries = FirstNonBlank(GetRequestValue(fields, "series"));
            var renamedAuthor = FirstNonBlank(GetRequestValue(fields, "author"), GetRequestValue(fields, "creator"));
            return new MembershipPlan(
                Action: "rename_container",
                MediaType: mediaType,
                CurrentEntityId: entityRow.WorkId,
                CurrentParentEntityId: entityRow.ParentWorkId,
                CurrentRootEntityId: entityRow.RootWorkId,
                CurrentOrdinal: entityRow.Ordinal,
                RequestedTitle: renamedSeries,
                RequestedParentLabel: renamedSeries,
                RequestedSecondaryLabel: renamedAuthor,
                RequestedParentKey: mediaType == "Comics"
                    ? BuildHierarchyParentKey(renamedSeries)
                    : BuildHierarchyParentKey(renamedAuthor, renamedSeries),
                RequestedOrdinal: entityRow.Ordinal,
                SelectedPrimaryTargetId: null,
                SelectedSecondaryTargetId: null);
        }

        return new MembershipPlan(
            Action: "none",
            MediaType: mediaType,
            CurrentEntityId: entityRow.WorkId,
            CurrentParentEntityId: entityRow.ParentWorkId,
            CurrentRootEntityId: entityRow.RootWorkId,
            CurrentOrdinal: entityRow.Ordinal,
            RequestedTitle: null,
            RequestedParentLabel: null,
            RequestedSecondaryLabel: null,
            RequestedParentKey: null,
            RequestedOrdinal: entityRow.Ordinal,
            SelectedPrimaryTargetId: null,
            SelectedSecondaryTargetId: null);
    }

    private static async Task<MembershipPreviewEnvelope> FinalizeMembershipPreviewAsync(
        SqliteConnection conn,
        MembershipPlan plan,
        MembershipPreviewRequest request,
        CancellationToken ct) =>
        await FinalizeMembershipPlanAsync(conn, null, plan, request, applyChanges: false, ct);

    private static async Task<MembershipPreviewEnvelope> FinalizeMembershipPlanAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        MembershipPlan plan,
        MembershipPreviewRequest request,
        bool applyChanges,
        CancellationToken ct)
    {
        var currentPath = await BuildMembershipPathAsync(conn, plan.CurrentEntityId, tx, ct);

        if (string.Equals(plan.Action, "none", StringComparison.OrdinalIgnoreCase))
        {
            return new MembershipPreviewEnvelope("none", currentPath, currentPath, false, false, false, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "No structural change is needed.", null);
        }

        if (string.Equals(plan.Action, "rename_container", StringComparison.OrdinalIgnoreCase))
        {
            var currentKey = conn.QueryFirstOrDefault<string>("SELECT parent_key FROM works WHERE id = @workId LIMIT 1;", new { workId = plan.CurrentEntityId.ToString() }, tx);
            if (string.IsNullOrWhiteSpace(plan.RequestedParentKey) || string.Equals(currentKey, plan.RequestedParentKey, StringComparison.OrdinalIgnoreCase))
                return new MembershipPreviewEnvelope("none", currentPath, currentPath, false, false, false, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "No container identity change was detected.", null);

            if (applyChanges)
            {
                conn.Execute("UPDATE works SET parent_key = @parentKey WHERE id = @workId;", new { parentKey = plan.RequestedParentKey, workId = plan.CurrentEntityId.ToString() }, tx);
                await UpsertContainerIdentityAsync(conn, tx!, plan);
            }

            return new MembershipPreviewEnvelope("rename_container", currentPath, BuildRenamedContainerPath(plan), false, true, applyChanges, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "The container identity will be updated.", null);
        }

        if (string.Equals(plan.Action, "move_season", StringComparison.OrdinalIgnoreCase))
        {
            if (!plan.CurrentParentEntityId.HasValue || !plan.RequestedOrdinal.HasValue || plan.RequestedOrdinal == plan.CurrentOrdinal)
                return new MembershipPreviewEnvelope("none", currentPath, currentPath, false, false, false, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "No season move is needed.", null);

            var conflictId = FindChildByOrdinal(conn, plan.CurrentParentEntityId.Value, plan.RequestedOrdinal.Value, tx);
            if (conflictId.HasValue && conflictId.Value != plan.CurrentEntityId)
                return new MembershipPreviewEnvelope("conflict", currentPath, $"Season {plan.RequestedOrdinal.Value}", false, false, false, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "The requested season number is already occupied.", "Another owned season already uses that season number.");

            if (applyChanges)
            {
                conn.Execute(
                    "UPDATE works SET ordinal = @ordinal, parent_key = @parentKey WHERE id = @workId;",
                    new
                    {
                        ordinal = plan.RequestedOrdinal.Value,
                        parentKey = BuildHierarchyParentKey(plan.RequestedParentLabel, $"S{plan.RequestedOrdinal.Value:D2}"),
                        workId = plan.CurrentEntityId.ToString(),
                    },
                    tx);
            }

            return new MembershipPreviewEnvelope("move_season", currentPath, $"Season {plan.RequestedOrdinal.Value}", false, true, applyChanges, plan.CurrentEntityId, plan.CurrentRootEntityId, plan.CurrentParentEntityId, "The season will move to the requested position.", null);
        }

        var resolvedTarget = await ResolveMembershipMoveTargetAsync(conn, tx, plan, applyChanges, ct);
        if (!resolvedTarget.CanApply)
            return new MembershipPreviewEnvelope(resolvedTarget.Action, currentPath, resolvedTarget.TargetPath, resolvedTarget.RequiresNewTarget, false, false, plan.CurrentEntityId, plan.CurrentRootEntityId, resolvedTarget.TargetParentEntityId, resolvedTarget.Message, resolvedTarget.ConflictMessage);

        if (applyChanges && resolvedTarget.TargetParentEntityId.HasValue)
        {
            conn.Execute(
                "UPDATE works SET parent_work_id = @parentId, ordinal = @ordinal WHERE id = @workId;",
                new { parentId = resolvedTarget.TargetParentEntityId.Value.ToString(), ordinal = (object?)plan.RequestedOrdinal ?? DBNull.Value, workId = plan.CurrentEntityId.ToString() },
                tx);
        }

        var targetRootEntityId = await ResolveRootWorkIdAsync(conn, resolvedTarget.TargetParentEntityId ?? plan.CurrentEntityId, tx, ct);
        return new MembershipPreviewEnvelope(resolvedTarget.Action, currentPath, resolvedTarget.TargetPath, resolvedTarget.RequiresNewTarget, true, applyChanges, plan.CurrentEntityId, targetRootEntityId, resolvedTarget.TargetParentEntityId, resolvedTarget.Message, null);
    }

    private static async Task<ResolvedMoveTarget> ResolveMembershipMoveTargetAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        MembershipPlan plan,
        bool applyChanges,
        CancellationToken ct)
    {
        Guid? targetParentId = null;
        var requiresNewTarget = false;
        string targetPath;

        if (plan.MediaType == "TV")
        {
            var showId = plan.SelectedPrimaryTargetId ?? FindParentByKey(conn, plan.MediaType, plan.RequestedParentKey, tx);
            if (!showId.HasValue)
            {
                requiresNewTarget = true;
                if (!applyChanges)
                    return new ResolvedMoveTarget("move_child", null, BuildTelevisionTargetPath(plan), true, true, "The episode will move into a new show and season.", null);

                showId = InsertParentWork(conn, tx!, plan.MediaType, plan.RequestedParentKey!, null, null);
                await UpsertCanonicalValuesAsync(conn, tx!, showId.Value, new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["show_name"] = plan.RequestedParentLabel });
            }

            var seasonNumber = ParseNavigatorOrdinal(plan.RequestedSecondaryLabel, null);
            if (plan.RequestedOrdinal is null)
                return new ResolvedMoveTarget("conflict", null, string.Empty, false, false, "An episode number is required for TV membership moves.", "Episode number is required.");

            targetParentId = plan.SelectedSecondaryTargetId;
            if (!targetParentId.HasValue && seasonNumber.HasValue)
            {
                targetParentId = FindChildByOrdinal(conn, showId.Value, seasonNumber.Value, tx);
                if (!targetParentId.HasValue)
                {
                    requiresNewTarget = true;
                    if (!applyChanges)
                        return new ResolvedMoveTarget("move_child", null, BuildTelevisionTargetPath(plan), true, true, "The episode will move into a new season.", null);

                    targetParentId = InsertParentWork(conn, tx!, plan.MediaType, BuildHierarchyParentKey(plan.RequestedParentLabel, $"S{seasonNumber.Value:D2}"), showId.Value, seasonNumber);
                    await UpsertCanonicalValuesAsync(conn, tx!, targetParentId.Value, new Dictionary<string, string?> { ["season_number"] = seasonNumber.Value.ToString(CultureInfo.InvariantCulture) });
                }
            }

            targetParentId ??= showId;
            targetPath = BuildTelevisionTargetPath(plan);
        }
        else if (plan.MediaType == "Music")
        {
            targetParentId = plan.SelectedPrimaryTargetId ?? FindParentByKey(conn, plan.MediaType, plan.RequestedParentKey, tx);
            if (!targetParentId.HasValue)
            {
                requiresNewTarget = true;
                if (!applyChanges)
                    return new ResolvedMoveTarget("move_child", null, BuildMusicTargetPath(plan), true, true, "The track will move into a new album.", null);

                targetParentId = InsertParentWork(conn, tx!, plan.MediaType, plan.RequestedParentKey!, null, null);
                await UpsertCanonicalValuesAsync(conn, tx!, targetParentId.Value, new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["album"] = plan.RequestedParentLabel, ["artist"] = plan.RequestedSecondaryLabel });
            }

            targetPath = BuildMusicTargetPath(plan);
        }
        else
        {
            targetParentId = plan.SelectedPrimaryTargetId ?? FindParentByKey(conn, plan.MediaType, plan.RequestedParentKey, tx);
            if (!targetParentId.HasValue)
            {
                requiresNewTarget = true;
                if (!applyChanges)
                    return new ResolvedMoveTarget("move_child", null, BuildSeriesTargetPath(plan), true, true, $"The {GetSeriesLeafLabel(plan.MediaType).ToLowerInvariant()} will move into a new series.", null);

                targetParentId = InsertParentWork(conn, tx!, plan.MediaType, plan.RequestedParentKey!, null, null);
                await UpsertCanonicalValuesAsync(conn, tx!, targetParentId.Value, new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["series"] = plan.RequestedParentLabel, ["author"] = plan.RequestedSecondaryLabel });
            }

            targetPath = BuildSeriesTargetPath(plan);
        }

        if (targetParentId.HasValue && plan.RequestedOrdinal.HasValue)
        {
            var conflictId = FindChildByOrdinal(conn, targetParentId.Value, plan.RequestedOrdinal.Value, tx);
            if (conflictId.HasValue && conflictId.Value != plan.CurrentEntityId)
                return new ResolvedMoveTarget("conflict", targetParentId, targetPath, requiresNewTarget, false, "The requested position is already occupied.", "Another owned item already uses that ordinal.");
        }

        if (targetParentId == plan.CurrentParentEntityId && plan.RequestedOrdinal == plan.CurrentOrdinal)
            return new ResolvedMoveTarget("none", targetParentId, targetPath, requiresNewTarget, false, "No structural change is needed.", null);

        return new ResolvedMoveTarget("move_child", targetParentId, targetPath, requiresNewTarget, true, "The item will move to the selected membership target.", null);
    }

    private static async Task<string> BuildMembershipPathAsync(SqliteConnection conn, Guid entityId, SqliteTransaction? tx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var launch = conn.QueryFirstOrDefault<MembershipEntityRow>("""
            SELECT w.id             AS WorkId,
                   w.media_type     AS MediaType,
                   w.work_kind      AS WorkKind,
                   w.parent_work_id AS ParentWorkId,
                   w.ordinal        AS Ordinal,
                   w.parent_key     AS ParentKey,
                   COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() }, tx);

        if (launch is null)
            return string.Empty;

        var parts = new List<string>();
        var currentId = launch.WorkId;
        while (true)
        {
            var row = conn.QueryFirstOrDefault<NavigatorValueRow>("""
                SELECT w.id AS WorkId,
                       NULL AS AssetId,
                       MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS WorkTitle,
                       MAX(CASE WHEN cv.key = 'show_name' THEN cv.value END) AS WorkShowName,
                       MAX(CASE WHEN cv.key = 'network' THEN cv.value END) AS WorkNetwork,
                       MAX(CASE WHEN cv.key = 'album' THEN cv.value END) AS WorkAlbum,
                       MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS WorkArtist,
                       MAX(CASE WHEN cv.key = 'series' THEN cv.value END) AS WorkSeries,
                       MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS WorkAuthor,
                       MAX(CASE WHEN cv.key = 'year' THEN cv.value END) AS WorkYear,
                       MAX(CASE WHEN cv.key = 'season_number' THEN cv.value END) AS WorkSeasonNumber,
                       NULL AS AssetTitle,
                       NULL AS AssetEpisodeTitle,
                       NULL AS AssetEpisodeNumber,
                       NULL AS AssetSeasonNumber,
                       NULL AS AssetTrackNumber,
                       NULL AS AssetSeriesPosition,
                       NULL AS AssetIssueNumber,
                       NULL AS AssetVolume,
                       w.parent_key AS ParentKey
                FROM works w
                LEFT JOIN canonical_values cv ON cv.entity_id = w.id
                WHERE w.id = @workId
                GROUP BY w.id, w.parent_key;
                """, new { workId = currentId.ToString() }, tx);

            var treeRow = conn.QueryFirstOrDefault<NavigatorTreeRow>("SELECT id AS WorkId, parent_work_id AS ParentWorkId, 0 AS Depth, work_kind AS WorkKind, ordinal AS Ordinal, is_catalog_only AS IsCatalogOnly, parent_key AS ParentKey FROM works WHERE id = @workId LIMIT 1;", new { workId = currentId.ToString() }, tx);
            if (treeRow is null)
                break;

            parts.Insert(0, ResolveNavigatorTitle(NormalizeEditorMediaType(launch.MediaType), treeRow, row));
            if (!treeRow.ParentWorkId.HasValue)
                break;

            currentId = treeRow.ParentWorkId.Value;
        }

        return string.Join(" / ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static async Task<Guid> ResolveRootWorkIdAsync(SqliteConnection conn, Guid entityId, SqliteTransaction? tx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var rootId = conn.QueryFirstOrDefault<string>("""
            SELECT COALESCE(gp.id, p.id, w.id) AS RootWorkId
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            WHERE w.id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() }, tx);

        return TryParseGuid(rootId) ?? entityId;
    }

    private static Guid? FindParentByKey(SqliteConnection conn, string mediaType, string? parentKey, SqliteTransaction? tx)
    {
        if (string.IsNullOrWhiteSpace(parentKey))
            return null;

        var id = conn.QueryFirstOrDefault<string>("SELECT id FROM works WHERE media_type = @mediaType AND work_kind = 'parent' AND parent_key = @parentKey LIMIT 1;", new { mediaType, parentKey }, tx);
        return TryParseGuid(id);
    }

    private static Guid? FindChildByOrdinal(SqliteConnection conn, Guid parentWorkId, int ordinal, SqliteTransaction? tx)
    {
        var id = conn.QueryFirstOrDefault<string>("SELECT id FROM works WHERE parent_work_id = @parentWorkId AND ordinal = @ordinal LIMIT 1;", new { parentWorkId = parentWorkId.ToString(), ordinal }, tx);
        return TryParseGuid(id);
    }

    private static Guid InsertParentWork(SqliteConnection conn, SqliteTransaction tx, string mediaType, string parentKey, Guid? grandparentWorkId, int? ordinal)
    {
        var workId = Guid.NewGuid();
        conn.Execute(
            """
            INSERT INTO works
                (id, collection_id, media_type, work_kind, parent_work_id, ordinal, is_catalog_only, parent_key, wikidata_status)
            VALUES
                (@id, NULL, @mediaType, 'parent', @parentWorkId, @ordinal, 0, @parentKey, 'pending');
            """,
            new { id = workId.ToString(), mediaType, parentWorkId = grandparentWorkId?.ToString(), ordinal, parentKey },
            tx);
        return workId;
    }

    private static Task UpsertContainerIdentityAsync(SqliteConnection conn, SqliteTransaction tx, MembershipPlan plan) =>
        UpsertCanonicalValuesAsync(
            conn,
            tx,
            plan.CurrentEntityId,
            plan.MediaType switch
            {
                "TV" => new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["show_name"] = plan.RequestedParentLabel },
                "Music" => new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["album"] = plan.RequestedParentLabel, ["artist"] = plan.RequestedSecondaryLabel },
                "Comics" or "Books" or "Audiobooks" => new Dictionary<string, string?> { ["title"] = plan.RequestedParentLabel, ["series"] = plan.RequestedParentLabel, ["author"] = plan.RequestedSecondaryLabel },
                _ => new Dictionary<string, string?>(),
            });

    private static Task UpsertCanonicalValuesAsync(SqliteConnection conn, SqliteTransaction tx, Guid entityId, IReadOnlyDictionary<string, string?> values)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                continue;

            conn.Execute(
                """
                INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted)
                VALUES (@entityId, @key, @value, @lastScoredAt, 0)
                ON CONFLICT(entity_id, key) DO UPDATE SET
                    value = excluded.value,
                    last_scored_at = excluded.last_scored_at,
                    is_conflicted = 0;
                """,
                new { entityId = entityId.ToString(), key, value, lastScoredAt = now },
                tx);
        }

        return Task.CompletedTask;
    }

    private static string BuildTelevisionTargetPath(MembershipPlan plan)
    {
        var show = string.IsNullOrWhiteSpace(plan.RequestedParentLabel) ? "Show" : plan.RequestedParentLabel;
        var season = ParseNavigatorOrdinal(plan.RequestedSecondaryLabel, null) is int seasonNumber ? $"Season {seasonNumber}" : "Season";
        var episode = plan.RequestedOrdinal.HasValue ? $"Episode {plan.RequestedOrdinal.Value}" : "Episode";
        return $"{show} / {season} / {episode}";
    }

    private static string BuildMusicTargetPath(MembershipPlan plan)
    {
        var artist = string.IsNullOrWhiteSpace(plan.RequestedSecondaryLabel) ? "Artist" : plan.RequestedSecondaryLabel;
        var album = string.IsNullOrWhiteSpace(plan.RequestedParentLabel) ? "Album" : plan.RequestedParentLabel;
        var track = plan.RequestedOrdinal.HasValue ? $"Track {plan.RequestedOrdinal.Value}" : "Track";
        return $"{artist} / {album} / {track}";
    }

    private static string BuildSeriesTargetPath(MembershipPlan plan)
    {
        var series = string.IsNullOrWhiteSpace(plan.RequestedParentLabel) ? "Series" : plan.RequestedParentLabel;
        var leaf = plan.RequestedOrdinal.HasValue ? $"{GetSeriesLeafLabel(plan.MediaType)} {plan.RequestedOrdinal.Value}" : GetSeriesLeafLabel(plan.MediaType);
        return string.IsNullOrWhiteSpace(plan.RequestedSecondaryLabel)
            ? $"{series} / {leaf}"
            : $"{plan.RequestedSecondaryLabel} / {series} / {leaf}";
    }

    private static string BuildRenamedContainerPath(MembershipPlan plan) =>
        string.IsNullOrWhiteSpace(plan.RequestedSecondaryLabel)
            ? plan.RequestedParentLabel ?? "Container"
            : $"{plan.RequestedSecondaryLabel} / {plan.RequestedParentLabel}";

    private static string GetSeriesLeafLabel(string mediaType) =>
        mediaType switch
        {
            "Comics" => "Issue",
            "Audiobooks" => "Audiobook",
            _ => "Book",
        };

    private static string BuildHierarchyParentKey(params string?[] parts)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            if (!first)
                sb.Append('|');

            sb.Append(NormalizeHierarchyPart(part));
            first = false;
        }

        return sb.ToString();
    }

    private static string NormalizeHierarchyPart(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var prevSpace = false;
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!prevSpace && sb.Length > 0)
                    sb.Append(' ');

                prevSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                prevSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string? FormatParentKeyFallback(string? parentKey) =>
        string.IsNullOrWhiteSpace(parentKey)
            ? null
            : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(parentKey.Replace('|', ' '));

    private static string BuildDelimitedLabel(params string?[] values)
    {
        var parts = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? string.Empty : string.Join(" • ", parts);
    }

    private static int? ParseNavigatorOrdinal(string? raw, int? fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        var digits = new string(raw.Trim().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : fallback;
    }

    private static string? GetRequestValue(IReadOnlyDictionary<string, string?> fields, string key) =>
        fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static Guid? GetRequestTarget(IReadOnlyDictionary<string, Guid?> targets, string key) =>
        targets.TryGetValue(key, out var value) && value.HasValue && value.Value != Guid.Empty ? value.Value : null;

    private sealed record NavigatorTreeRow(Guid WorkId, Guid? ParentWorkId, int Depth, string WorkKind, int? Ordinal, bool IsCatalogOnly, string? ParentKey);
    private sealed record NavigatorValueRow(Guid WorkId, Guid? AssetId, string? WorkTitle, string? WorkShowName, string? WorkNetwork, string? WorkAlbum, string? WorkArtist, string? WorkSeries, string? WorkAuthor, string? WorkYear, string? WorkSeasonNumber, string? AssetTitle, string? AssetEpisodeTitle, string? AssetEpisodeNumber, string? AssetSeasonNumber, string? AssetTrackNumber, string? AssetSeriesPosition, string? AssetIssueNumber, string? AssetVolume, string? ParentKey);
    private sealed record MembershipEntityRow(Guid WorkId, string MediaType, string WorkKind, Guid? ParentWorkId, int? Ordinal, string? ParentKey, Guid RootWorkId);
    private sealed record MembershipPlan(string Action, string MediaType, Guid CurrentEntityId, Guid? CurrentParentEntityId, Guid CurrentRootEntityId, int? CurrentOrdinal, string? RequestedTitle, string? RequestedParentLabel, string? RequestedSecondaryLabel, string? RequestedParentKey, int? RequestedOrdinal, Guid? SelectedPrimaryTargetId, Guid? SelectedSecondaryTargetId);
    private sealed record ResolvedMoveTarget(string Action, Guid? TargetParentEntityId, string TargetPath, bool RequiresNewTarget, bool CanApply, string Message, string? ConflictMessage);
    private sealed record MembershipPreviewRequest([property: JsonPropertyName("scope_id")] string? ScopeId, [property: JsonPropertyName("field_values")] Dictionary<string, string?>? FieldValues, [property: JsonPropertyName("selected_target_ids")] Dictionary<string, Guid?>? SelectedTargetIds);
    private sealed record MediaEditorNavigatorEnvelope([property: JsonPropertyName("enabled")] bool Enabled, [property: JsonPropertyName("media_type")] string MediaType, [property: JsonPropertyName("container_entity_id")] Guid ContainerEntityId, [property: JsonPropertyName("selected_entity_id")] Guid SelectedEntityId, [property: JsonPropertyName("container_label")] string ContainerLabel, [property: JsonPropertyName("container_title")] string ContainerTitle, [property: JsonPropertyName("container_subtitle")] string? ContainerSubtitle, [property: JsonPropertyName("nodes")] IReadOnlyList<MediaEditorNavigatorNodeEnvelope> Nodes);
    private sealed record MediaEditorNavigatorNodeEnvelope([property: JsonPropertyName("node_id")] Guid NodeId, [property: JsonPropertyName("parent_node_id")] Guid? ParentNodeId, [property: JsonPropertyName("entity_id")] Guid EntityId, [property: JsonPropertyName("scope_id")] string ScopeId, [property: JsonPropertyName("node_kind")] string NodeKind, [property: JsonPropertyName("label")] string Label, [property: JsonPropertyName("title")] string Title, [property: JsonPropertyName("subtitle")] string? Subtitle, [property: JsonPropertyName("ordinal_label")] string? OrdinalLabel, [property: JsonPropertyName("depth")] int Depth, [property: JsonPropertyName("is_root")] bool IsRoot, [property: JsonPropertyName("is_leaf")] bool IsLeaf, [property: JsonPropertyName("is_owned")] bool IsOwned, [property: JsonPropertyName("can_quarantine")] bool CanQuarantine, [property: JsonPropertyName("quarantine_count")] int QuarantineCount);
    private sealed record MembershipSuggestionEnvelope([property: JsonPropertyName("entity_id")] Guid? EntityId, [property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("label")] string Label, [property: JsonPropertyName("subtitle")] string? Subtitle);
    private sealed record MembershipPreviewEnvelope([property: JsonPropertyName("action")] string Action, [property: JsonPropertyName("current_path")] string CurrentPath, [property: JsonPropertyName("target_path")] string TargetPath, [property: JsonPropertyName("requires_new_target")] bool RequiresNewTarget, [property: JsonPropertyName("can_apply")] bool CanApply, [property: JsonPropertyName("applied")] bool Applied, [property: JsonPropertyName("selected_entity_id")] Guid SelectedEntityId, [property: JsonPropertyName("target_root_entity_id")] Guid TargetRootEntityId, [property: JsonPropertyName("target_parent_entity_id")] Guid? TargetParentEntityId, [property: JsonPropertyName("message")] string Message, [property: JsonPropertyName("conflict_message")] string? ConflictMessage);
}
