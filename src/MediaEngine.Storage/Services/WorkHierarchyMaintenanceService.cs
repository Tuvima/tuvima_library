using System.Globalization;
using System.Text;
using Dapper;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Logging;

namespace MediaEngine.Storage.Services;

public sealed class WorkHierarchyMaintenanceService
{
    private readonly IDatabaseConnection _db;
    private readonly ILogger<WorkHierarchyMaintenanceService>? _logger;

    public WorkHierarchyMaintenanceService(
        IDatabaseConnection db,
        ILogger<WorkHierarchyMaintenanceService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
        _logger = logger;
    }

    public async Task<HierarchyRepairResult> RepairLegacyTvAndMusicAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        var reparented = 0;
        var parentsCreated = 0;

        foreach (var raw in conn.Query(LegacyTvSql, transaction: tx))
        {
            var row = new LegacyTvRow(
                ReadString(raw.WorkId),
                ReadString(raw.CollectionId),
                ReadString(raw.ShowName),
                ReadInt(raw.SeasonNumber),
                ReadInt(raw.EpisodeNumber),
                ReadString(raw.EpisodeTitle));
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.ShowName) || !HasEpisodeSignal(row))
            {
                continue;
            }

            var showKey = BuildParentKey(row.ShowName);
            var showId = FindOrCreateParent(
                conn,
                tx,
                "TV",
                showKey,
                null,
                null,
                row.CollectionId,
                out var showCreated);
            parentsCreated += showCreated ? 1 : 0;
            UpsertCanonical(conn, tx, showId, "show_name", row.ShowName);
            UpsertCanonical(conn, tx, showId, "title", row.ShowName);

            var targetParentId = showId;
            if (row.SeasonNumber is { } seasonNumber)
            {
                var seasonKey = BuildParentKey(row.ShowName, $"S{seasonNumber:D2}");
                targetParentId = FindOrCreateSeasonParent(
                    conn,
                    tx,
                    showId,
                    seasonNumber,
                    seasonKey,
                    row.CollectionId,
                    out var seasonCreated);
                parentsCreated += seasonCreated ? 1 : 0;
                UpsertCanonical(conn, tx, targetParentId, "title", $"{row.ShowName} Season {seasonNumber}");
                UpsertCanonical(conn, tx, targetParentId, "show_name", row.ShowName);
                UpsertCanonical(conn, tx, targetParentId, "season_number", seasonNumber.ToString(CultureInfo.InvariantCulture));
            }

            reparented += conn.Execute("""
                UPDATE works
                SET parent_work_id = @parentId,
                    work_kind = 'child',
                    ordinal = COALESCE(ordinal, @ordinal),
                    is_catalog_only = 0,
                    ownership = 'Owned'
                WHERE id = @workId
                  AND (parent_work_id IS NULL OR parent_work_id != @parentId);
                """,
                new
                {
                    parentId = targetParentId.ToString("D"),
                    workId = row.WorkId,
                    ordinal = row.EpisodeNumber,
                },
                tx);
        }

        foreach (var raw in conn.Query(LegacyMusicSql, transaction: tx))
        {
            var row = new LegacyMusicRow(
                ReadString(raw.WorkId),
                ReadString(raw.CollectionId),
                ReadString(raw.Album),
                ReadString(raw.Artist),
                ReadInt(raw.TrackNumber));
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(row.Album))
            {
                continue;
            }

            var albumKey = BuildParentKey(row.Artist, row.Album);
            var albumId = FindOrCreateParent(
                conn,
                tx,
                "Music",
                albumKey,
                null,
                null,
                row.CollectionId,
                out var albumCreated);
            parentsCreated += albumCreated ? 1 : 0;
            UpsertCanonical(conn, tx, albumId, "album", row.Album);
            UpsertCanonical(conn, tx, albumId, "title", row.Album);
            if (!string.IsNullOrWhiteSpace(row.Artist))
            {
                UpsertCanonical(conn, tx, albumId, "artist", row.Artist);
            }

            reparented += conn.Execute("""
                UPDATE works
                SET parent_work_id = @parentId,
                    work_kind = 'child',
                    ordinal = COALESCE(ordinal, @ordinal),
                    is_catalog_only = 0,
                    ownership = 'Owned'
                WHERE id = @workId
                  AND (parent_work_id IS NULL OR parent_work_id != @parentId);
                """,
                new
                {
                    parentId = albumId.ToString("D"),
                    workId = row.WorkId,
                    ordinal = row.TrackNumber,
                },
                tx);
        }

        tx.Commit();
        if (parentsCreated > 0 || reparented > 0)
        {
            _logger?.LogInformation(
                "Repaired legacy work hierarchy: {ParentsCreated} parent rows created, {Reparented} child rows reparented",
                parentsCreated,
                reparented);
        }

        var pruned = await CleanupEmptyParentsAsync(ct).ConfigureAwait(false);
        return new HierarchyRepairResult(parentsCreated, reparented, pruned);
    }

    public Task<int> CleanupEmptyParentsAsync(Guid? startingParentId, CancellationToken ct = default)
        => CleanupEmptyParentsAsync(ct);

    public Task<int> CleanupEmptyParentsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        using var tx = conn.BeginTransaction();

        var deleted = 0;
        while (true)
        {
            var parentIds = conn.Query<string>(
                EmptyParentSql,
                transaction: tx).ToList();
            if (parentIds.Count == 0)
            {
                break;
            }

            foreach (var parentId in parentIds)
            {
                DeleteParentDerivedState(conn, tx, parentId);
                deleted += conn.Execute("DELETE FROM works WHERE id = @parentId;", new { parentId }, tx);
            }
        }

        tx.Commit();
        return Task.FromResult(deleted);
    }

    private static readonly string LegacyTvSql = """
        SELECT w.id AS WorkId,
               w.collection_id AS CollectionId,
               COALESCE(NULLIF(show_work.value, ''), NULLIF(show_asset.value, '')) AS ShowName,
               CAST(COALESCE(NULLIF(season_work.value, ''), NULLIF(season_asset.value, '')) AS INTEGER) AS SeasonNumber,
               CAST(COALESCE(NULLIF(episode_work.value, ''), NULLIF(episode_asset.value, ''), w.ordinal) AS INTEGER) AS EpisodeNumber,
               COALESCE(NULLIF(ep_title_work.value, ''), NULLIF(ep_title_asset.value, ''), NULLIF(title_asset.value, ''), NULLIF(title_work.value, '')) AS EpisodeTitle
        FROM works w
        LEFT JOIN editions e ON e.work_id = w.id
        LEFT JOIN media_assets ma ON ma.edition_id = e.id
        LEFT JOIN canonical_values show_work ON show_work.entity_id = w.id AND show_work.key = 'show_name'
        LEFT JOIN canonical_values show_asset ON show_asset.entity_id = ma.id AND show_asset.key = 'show_name'
        LEFT JOIN canonical_values season_work ON season_work.entity_id = w.id AND season_work.key = 'season_number'
        LEFT JOIN canonical_values season_asset ON season_asset.entity_id = ma.id AND season_asset.key = 'season_number'
        LEFT JOIN canonical_values episode_work ON episode_work.entity_id = w.id AND episode_work.key = 'episode_number'
        LEFT JOIN canonical_values episode_asset ON episode_asset.entity_id = ma.id AND episode_asset.key = 'episode_number'
        LEFT JOIN canonical_values ep_title_work ON ep_title_work.entity_id = w.id AND ep_title_work.key = 'episode_title'
        LEFT JOIN canonical_values ep_title_asset ON ep_title_asset.entity_id = ma.id AND ep_title_asset.key = 'episode_title'
        LEFT JOIN canonical_values title_work ON title_work.entity_id = w.id AND title_work.key = 'title'
        LEFT JOIN canonical_values title_asset ON title_asset.entity_id = ma.id AND title_asset.key = 'title'
        WHERE w.media_type = 'TV'
          AND w.parent_work_id IS NULL
          AND w.work_kind NOT IN ('parent', 'catalog')
          AND COALESCE(w.is_catalog_only, 0) = 0;
        """;

    private static readonly string LegacyMusicSql = """
        SELECT w.id AS WorkId,
               w.collection_id AS CollectionId,
               COALESCE(NULLIF(album_work.value, ''), NULLIF(album_asset.value, '')) AS Album,
               COALESCE(NULLIF(artist_work.value, ''), NULLIF(album_artist_work.value, ''), NULLIF(artist_asset.value, ''), NULLIF(album_artist_asset.value, '')) AS Artist,
               CAST(COALESCE(NULLIF(track_work.value, ''), NULLIF(track_asset.value, ''), w.ordinal) AS INTEGER) AS TrackNumber
        FROM works w
        LEFT JOIN editions e ON e.work_id = w.id
        LEFT JOIN media_assets ma ON ma.edition_id = e.id
        LEFT JOIN canonical_values album_work ON album_work.entity_id = w.id AND album_work.key = 'album'
        LEFT JOIN canonical_values album_asset ON album_asset.entity_id = ma.id AND album_asset.key = 'album'
        LEFT JOIN canonical_values artist_work ON artist_work.entity_id = w.id AND artist_work.key = 'artist'
        LEFT JOIN canonical_values album_artist_work ON album_artist_work.entity_id = w.id AND album_artist_work.key = 'album_artist'
        LEFT JOIN canonical_values artist_asset ON artist_asset.entity_id = ma.id AND artist_asset.key = 'artist'
        LEFT JOIN canonical_values album_artist_asset ON album_artist_asset.entity_id = ma.id AND album_artist_asset.key = 'album_artist'
        LEFT JOIN canonical_values track_work ON track_work.entity_id = w.id AND track_work.key IN ('track_number', 'track')
        LEFT JOIN canonical_values track_asset ON track_asset.entity_id = ma.id AND track_asset.key IN ('track_number', 'track')
        WHERE w.media_type = 'Music'
          AND w.parent_work_id IS NULL
          AND w.work_kind NOT IN ('parent', 'catalog')
          AND COALESCE(w.is_catalog_only, 0) = 0;
        """;

    private const string EmptyParentSql = """
        SELECT p.id
        FROM works p
        WHERE p.work_kind = 'parent'
          AND COALESCE(p.is_catalog_only, 0) = 0
          AND NOT EXISTS (SELECT 1 FROM editions e WHERE e.work_id = p.id)
          AND NOT EXISTS (SELECT 1 FROM works child WHERE child.parent_work_id = p.id)
          AND NOT EXISTS (SELECT 1 FROM collection_items ci WHERE ci.work_id = p.id)
          AND NOT EXISTS (SELECT 1 FROM entity_assets ea WHERE ea.entity_id = p.id AND COALESCE(ea.is_user_override, 0) = 1)
          AND COALESCE(NULLIF(p.display_overrides_json, ''), '') = '';
        """;

    private static bool HasEpisodeSignal(LegacyTvRow row)
        => row.SeasonNumber.HasValue
           || row.EpisodeNumber.HasValue
           || !string.IsNullOrWhiteSpace(row.EpisodeTitle);

    private static string? ReadString(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is byte[] bytes && bytes.Length == 0)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static int? ReadInt(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is byte[] bytes && bytes.Length == 0)
        {
            return null;
        }

        if (value is long longValue)
        {
            return checked((int)longValue);
        }

        if (value is int intValue)
        {
            return intValue;
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static Guid FindOrCreateParent(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        string mediaType,
        string parentKey,
        Guid? parentId,
        int? ordinal,
        string? collectionId,
        out bool created)
    {
        var existing = conn.QueryFirstOrDefault<string?>("""
            SELECT id
            FROM works
            WHERE media_type = @mediaType
              AND work_kind = 'parent'
              AND parent_key = @parentKey
            LIMIT 1;
            """,
            new { mediaType, parentKey },
            tx);

        if (Guid.TryParse(existing, out var existingId))
        {
            created = false;
            return existingId;
        }

        var id = Guid.NewGuid();
        conn.Execute("""
            INSERT INTO works (id, collection_id, media_type, work_kind, parent_work_id, ordinal, is_catalog_only, parent_key, ownership, wikidata_status)
            VALUES (@id, @collectionId, @mediaType, 'parent', @parentId, @ordinal, 0, @parentKey, 'Owned', 'pending');
            """,
            new
            {
                id = id.ToString("D"),
                collectionId,
                mediaType,
                parentId = parentId?.ToString("D"),
                ordinal,
                parentKey,
            },
            tx);
        created = true;
        return id;
    }

    private static Guid FindOrCreateSeasonParent(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid showId,
        int seasonNumber,
        string seasonKey,
        string? collectionId,
        out bool created)
    {
        var existing = conn.QueryFirstOrDefault<string?>("""
            SELECT id
            FROM works
            WHERE parent_work_id = @showId
              AND work_kind = 'parent'
              AND ordinal = @seasonNumber
            LIMIT 1;
            """,
            new { showId = showId.ToString("D"), seasonNumber },
            tx);

        if (Guid.TryParse(existing, out var existingId))
        {
            created = false;
            return existingId;
        }

        return FindOrCreateParent(conn, tx, "TV", seasonKey, showId, seasonNumber, collectionId, out created);
    }

    private static void UpsertCanonical(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        Guid entityId,
        string key,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        conn.Execute("""
            INSERT INTO canonical_values (entity_id, key, value, last_scored_at, is_conflicted, needs_review)
            VALUES (@entityId, @key, @value, @now, 0, 0)
            ON CONFLICT(entity_id, key) DO NOTHING;
            """,
            new
            {
                entityId = entityId.ToString("D"),
                key,
                value = value.Trim(),
                now = DateTimeOffset.UtcNow.ToString("O"),
            },
            tx);
    }

    private static void DeleteParentDerivedState(
        System.Data.IDbConnection conn,
        System.Data.IDbTransaction tx,
        string parentId)
    {
        conn.Execute("DELETE FROM entity_assets WHERE entity_id = @parentId AND COALESCE(is_user_override, 0) = 0;", new { parentId }, tx);
        conn.Execute("DELETE FROM canonical_values WHERE entity_id = @parentId;", new { parentId }, tx);
        conn.Execute("DELETE FROM metadata_claims WHERE entity_id = @parentId;", new { parentId }, tx);
        conn.Execute("DELETE FROM review_queue WHERE entity_id = @parentId;", new { parentId }, tx);
        conn.Execute("UPDATE series_manifest_items SET linked_work_id = NULL WHERE linked_work_id = @parentId;", new { parentId }, tx);
    }

    private static string BuildParentKey(params string?[] parts)
    {
        var sb = new StringBuilder();
        var first = true;
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            if (!first)
            {
                sb.Append('|');
            }

            sb.Append(Normalize(part));
            first = false;
        }

        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var previousSpace = false;
        foreach (var ch in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                }
                previousSpace = true;
            }
            else
            {
                sb.Append(char.ToLowerInvariant(ch));
                previousSpace = false;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private sealed record LegacyTvRow(
        string WorkId,
        string? CollectionId,
        string? ShowName,
        int? SeasonNumber,
        int? EpisodeNumber,
        string? EpisodeTitle);

    private sealed record LegacyMusicRow(
        string WorkId,
        string? CollectionId,
        string? Album,
        string? Artist,
        int? TrackNumber);
}

public sealed record HierarchyRepairResult(int ParentsCreated, int ChildrenReparented, int EmptyParentsRemoved);
