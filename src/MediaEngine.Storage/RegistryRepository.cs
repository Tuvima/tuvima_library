using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IRegistryRepository"/>.
/// </summary>
public sealed class RegistryRepository : IRegistryRepository
{
    private readonly IDatabaseConnection _db;

    public RegistryRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    public Task<RegistryPageResult> GetPageAsync(RegistryQuery query, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        var sql = BuildProjectionSql();
        var conditions = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Search))
            conditions.Add(@"(fd.entity_id IN (SELECT si.entity_id FROM search_index si WHERE search_index MATCH @ftsQuery) OR fd.file_name LIKE @search)");

        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            var types = query.MediaType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (types.Length == 1)
                conditions.Add("fd.media_type = @mediaType");
            else if (types.Length > 1)
            {
                var placeholders = string.Join(", ", types.Select((_, i) => $"@mt{i}"));
                conditions.Add($"fd.media_type IN ({placeholders})");
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (query.Status == "Approved")
                conditions.Add("fd.status IN ('Identified', 'Confirmed', 'RetailMatched', 'QidNoMatch', 'Edited')");
            else
                conditions.Add("fd.status = @status");
        }
        else if (!query.IncludeAll)
        {
            conditions.Add("fd.library_visibility = 'visible'");
        }

        if (query.MinConfidence.HasValue)
            conditions.Add("fd.confidence >= @minConfidence");

        if (!string.IsNullOrWhiteSpace(query.MatchSource))
            conditions.Add("fd.match_source = @matchSource");

        if (query.DuplicatesOnly)
            conditions.Add("fd.has_duplicate = 1");

        if (query.MissingUniverseOnly)
            conditions.Add("(fd.wikidata_status IN ('missing', 'manual') OR fd.status = 'QidNoMatch')");

        if (query.MaxDays.HasValue)
            conditions.Add($"fd.created_at >= datetime('now', '-{query.MaxDays.Value} days')");

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var orderBy = query.Sort switch
        {
            "oldest" => "ORDER BY fd.created_at ASC, fd.title ASC",
            "title" => "ORDER BY fd.title ASC, fd.created_at DESC",
            "-title" => "ORDER BY fd.title DESC, fd.created_at DESC",
            "-confidence" => "ORDER BY fd.confidence DESC, fd.title ASC",
            "confidence" => "ORDER BY fd.confidence ASC, fd.title ASC",
            _ => "ORDER BY fd.created_at DESC, fd.title ASC",
        };

        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = sql + $"SELECT COUNT(*) FROM full_data fd {whereClause};";
        AddParameters(countCmd, query);
        var totalCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var dataCmd = conn.CreateCommand();
        dataCmd.CommandText = sql + $"""
            SELECT
                fd.entity_id,
                fd.title,
                fd.year,
                fd.media_type,
                fd.cover_url,
                fd.match_source,
                fd.confidence,
                fd.status,
                fd.has_duplicate,
                fd.review_id,
                fd.review_trigger,
                fd.has_user_locks,
                fd.file_name,
                fd.author,
                fd.file_path,
                fd.wikidata_status,
                fd.wikidata_match,
                fd.retail_match,
                fd.wikidata_qid,
                fd.hero_url,
                fd.created_at,
                fd.director,
                fd.artist,
                fd.retail_match_detail,
                fd.series,
                fd.series_position,
                fd.narrator,
                fd.genre,
                fd.runtime,
                fd.rating,
                fd.album,
                fd.track_number,
                fd.season_number,
                fd.episode_number,
                fd.show_name,
                fd.duration,
                fd.episode_title,
                fd.network,
                fd.top_cast,
                fd.qid_resolution_method,
                fd.pipeline_step,
                fd.library_visibility,
                fd.is_ready_for_library,
                fd.artwork_state,
                fd.artwork_source,
                fd.artwork_settled_at
            FROM full_data fd
            {whereClause}
            {orderBy}
            LIMIT @limit OFFSET @offset;
            """;
        AddParameters(dataCmd, query);
        dataCmd.Parameters.AddWithValue("@limit", query.Limit);
        dataCmd.Parameters.AddWithValue("@offset", query.Offset);

        var items = new List<RegistryItem>();
        using var reader = dataCmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new RegistryItem
            {
                EntityId = Guid.Parse(reader.GetString(0)),
                Title = reader.GetString(1),
                Year = reader.IsDBNull(2) ? null : reader.GetString(2),
                MediaType = reader.IsDBNull(3) ? "" : reader.GetString(3),
                CoverUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                MatchSource = reader.IsDBNull(5) ? null : reader.GetString(5),
                Confidence = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6),
                Status = reader.IsDBNull(7) ? "Confirmed" : reader.GetString(7),
                HasDuplicate = !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
                ReviewItemId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
                ReviewTrigger = reader.IsDBNull(10) ? null : reader.GetString(10),
                HasUserLocks = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                FileName = reader.IsDBNull(12) ? null : reader.GetString(12),
                Author = reader.IsDBNull(13) ? null : reader.GetString(13),
                FilePath = reader.IsDBNull(14) ? null : reader.GetString(14),
                WikidataStatus = reader.IsDBNull(15) ? null : reader.GetString(15),
                WikidataMatch = reader.IsDBNull(16) ? "none" : reader.GetString(16),
                RetailMatch = reader.IsDBNull(17) ? "none" : reader.GetString(17),
                WikidataQid = reader.IsDBNull(18) ? null : reader.GetString(18),
                HeroUrl = reader.IsDBNull(19) ? null : reader.GetString(19),
                CreatedAt = reader.IsDBNull(20) ? DateTimeOffset.MinValue : ParseDateTimeOffset(reader.GetString(20)) ?? DateTimeOffset.MinValue,
                Director = reader.IsDBNull(21) ? null : reader.GetString(21),
                Artist = reader.IsDBNull(22) ? null : reader.GetString(22),
                RetailMatchDetail = reader.IsDBNull(23) ? null : reader.GetString(23),
                Series = reader.IsDBNull(24) ? null : reader.GetString(24),
                SeriesPosition = reader.IsDBNull(25) ? null : reader.GetString(25),
                Narrator = reader.IsDBNull(26) ? null : reader.GetString(26),
                Genre = reader.IsDBNull(27) ? null : reader.GetString(27),
                Runtime = reader.IsDBNull(28) ? null : reader.GetString(28),
                Rating = reader.IsDBNull(29) ? null : reader.GetString(29),
                Album = reader.IsDBNull(30) ? null : reader.GetString(30),
                TrackNumber = reader.IsDBNull(31) ? null : reader.GetString(31),
                SeasonNumber = reader.IsDBNull(32) ? null : reader.GetString(32),
                EpisodeNumber = reader.IsDBNull(33) ? null : reader.GetString(33),
                ShowName = reader.IsDBNull(34) ? null : reader.GetString(34),
                Duration = reader.IsDBNull(35) ? null : reader.GetString(35),
                EpisodeTitle = reader.IsDBNull(36) ? null : reader.GetString(36),
                Network = reader.IsDBNull(37) ? null : reader.GetString(37),
                TopCast = reader.IsDBNull(38) ? null : reader.GetString(38),
                QidResolutionMethod = reader.IsDBNull(39) ? null : reader.GetString(39),
                PipelineStep = reader.IsDBNull(40) ? "Retail" : reader.GetString(40),
                LibraryVisibility = reader.IsDBNull(41) ? "hidden" : reader.GetString(41),
                IsReadyForLibrary = !reader.IsDBNull(42) && reader.GetInt32(42) == 1,
                ArtworkState = reader.IsDBNull(43) ? "pending" : reader.GetString(43),
                ArtworkSource = reader.IsDBNull(44) ? null : reader.GetString(44),
                ArtworkSettledAt = reader.IsDBNull(45) ? null : ParseDateTimeOffset(reader.GetString(45)),
            });
        }

        return Task.FromResult(new RegistryPageResult(items, totalCount, query.Offset + items.Count < totalCount));
    }

    public Task<RegistryItemDetail?> GetDetailAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        var projection = conn.QueryFirstOrDefault<ProjectionRow>(
            BuildProjectionSql() + """
            SELECT
                fd.entity_id             AS EntityId,
                fd.title                 AS Title,
                fd.year                  AS Year,
                fd.media_type            AS MediaType,
                fd.cover_url             AS CoverUrl,
                fd.hero_url              AS HeroUrl,
                fd.confidence            AS Confidence,
                fd.status                AS Status,
                fd.match_source          AS MatchSource,
                fd.author                AS Author,
                fd.director              AS Director,
                fd.genre                 AS Genre,
                fd.runtime               AS Runtime,
                fd.description           AS Description,
                fd.series                AS Series,
                fd.series_position       AS SeriesPosition,
                fd.narrator              AS Narrator,
                fd.rating                AS Rating,
                fd.wikidata_qid          AS WikidataQid,
                fd.wikidata_status       AS WikidataStatus,
                fd.file_name             AS FileName,
                fd.file_path             AS FilePath,
                fd.review_id             AS ReviewId,
                fd.review_trigger        AS ReviewTrigger,
                fd.candidates_json       AS CandidatesJson,
                fd.has_user_locks        AS HasUserLocks,
                fd.pipeline_step         AS PipelineStep,
                fd.library_visibility      AS LibraryVisibility,
                fd.is_ready_for_library    AS IsReadyForLibrary,
                fd.artwork_state         AS ArtworkState,
                fd.artwork_source        AS ArtworkSource,
                fd.artwork_settled_at    AS ArtworkSettledAt,
                fd.qid_resolution_method AS QidResolutionMethod
            FROM full_data fd
            WHERE fd.entity_id = @entityId;
            """,
            new { entityId = entityId.ToString() });

        var lineageRow = conn.QueryFirstOrDefault<(string AssetId, string RootParentWorkId, string MediaType)>("""
            SELECT MIN(ma.id) AS AssetId,
                   COALESCE(gp.id, p.id, w.id) AS RootParentWorkId,
                   w.media_type AS MediaType
            FROM works w
            LEFT JOIN works p ON p.id = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN editions e ON e.work_id = w.id
            LEFT JOIN media_assets ma ON ma.edition_id = e.id
            WHERE w.id = @entityId
            GROUP BY w.id;
            """, new { entityId = entityId.ToString() });

        if (lineageRow == default || lineageRow.AssetId is null)
            return Task.FromResult<RegistryItemDetail?>(null);

        var assetIdStr = lineageRow.AssetId;
        var rootParentStr = lineageRow.RootParentWorkId;

        var selfValues = conn.Query<(string Key, string Value, int IsConflicted, string? WinningProviderId, int NeedsReview, string LastScoredAt)>("""
            SELECT key AS Key,
                   value AS Value,
                   is_conflicted AS IsConflicted,
                   winning_provider_id AS WinningProviderId,
                   needs_review AS NeedsReview,
                   last_scored_at AS LastScoredAt
            FROM canonical_values
            WHERE entity_id = @assetId
            ORDER BY key;
            """, new { assetId = assetIdStr })
            .Select(r => new RegistryCanonicalValue(
                Key: r.Key,
                Value: r.Value,
                IsConflicted: r.IsConflicted == 1,
                WinningProviderId: r.WinningProviderId,
                NeedsReview: r.NeedsReview == 1,
                LastScoredAt: ParseDateTimeOffset(r.LastScoredAt) ?? DateTimeOffset.MinValue))
            .ToList();

        var parentValues = conn.Query<(string Key, string Value, int IsConflicted, string? WinningProviderId, int NeedsReview, string LastScoredAt)>("""
            SELECT key AS Key,
                   value AS Value,
                   is_conflicted AS IsConflicted,
                   winning_provider_id AS WinningProviderId,
                   needs_review AS NeedsReview,
                   last_scored_at AS LastScoredAt
            FROM canonical_values
            WHERE entity_id = @parentId
            ORDER BY key;
            """, new { parentId = rootParentStr })
            .Select(r => new RegistryCanonicalValue(
                Key: r.Key,
                Value: r.Value,
                IsConflicted: r.IsConflicted == 1,
                WinningProviderId: r.WinningProviderId,
                NeedsReview: r.NeedsReview == 1,
                LastScoredAt: ParseDateTimeOffset(r.LastScoredAt) ?? DateTimeOffset.MinValue))
            .ToList();

        var canonicalValues = parentValues
            .Where(p => !selfValues.Any(s => s.Key == p.Key))
            .Concat(selfValues)
            .ToList();

        var claims = conn.Query<(string Id, string ClaimKey, string ClaimValue, string ProviderId, double Confidence, int IsUserLocked, string ClaimedAt)>("""
            SELECT id AS Id,
                   claim_key AS ClaimKey,
                   claim_value AS ClaimValue,
                   provider_id AS ProviderId,
                   confidence AS Confidence,
                   is_user_locked AS IsUserLocked,
                   claimed_at AS ClaimedAt
            FROM metadata_claims
            WHERE entity_id IN (@assetId, @parentId)
            ORDER BY claimed_at DESC;
            """, new { assetId = assetIdStr, parentId = rootParentStr })
            .Select(r => new RegistryClaimRecord(
                Id: Guid.Parse(r.Id),
                ClaimKey: r.ClaimKey,
                ClaimValue: r.ClaimValue,
                ProviderId: Guid.Parse(r.ProviderId),
                Confidence: r.Confidence,
                IsUserLocked: r.IsUserLocked == 1,
                ClaimedAt: ParseDateTimeOffset(r.ClaimedAt) ?? DateTimeOffset.MinValue))
            .ToList();

        var rqRow = conn.QueryFirstOrDefault<(string Id, string Trigger, double? ConfidenceScore, string? Detail, string? CandidatesJson)>("""
            SELECT id AS Id,
                   trigger AS Trigger,
                   confidence_score AS ConfidenceScore,
                   detail AS Detail,
                   candidates_json AS CandidatesJson
            FROM review_queue
            WHERE (entity_id = @assetId OR entity_id = @entityId) AND status = 'Pending'
            ORDER BY created_at DESC
            LIMIT 1;
            """, new { assetId = assetIdStr, entityId = entityId.ToString() });

        var maRow = conn.QueryFirstOrDefault<(string? FilePath, string? ContentHash)>("""
            SELECT ma.file_path_root AS FilePath,
                   ma.content_hash AS ContentHash
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id = @entityId
            LIMIT 1;
            """, new { entityId = entityId.ToString() });

        var matchLevel = conn.QueryFirstOrDefault<string?>("SELECT match_level FROM works WHERE id = @entityId;", new { entityId = entityId.ToString() }) ?? "work";

        string? Canonical(string key)
        {
            var value = canonicalValues.FirstOrDefault(v => v.Key == key)?.Value;
            if (value is null && key == "release_year")
                value = canonicalValues.FirstOrDefault(v => v.Key is "date" or "year")?.Value;
            return value;
        }

        var bridgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BridgeIdKeys.Isbn,
            BridgeIdKeys.Isbn13,
            BridgeIdKeys.Isbn10,
            BridgeIdKeys.Asin,
            BridgeIdKeys.TmdbId,
            BridgeIdKeys.ImdbId,
            BridgeIdKeys.WikidataQid,
            BridgeIdKeys.AppleBooksId,
            BridgeIdKeys.AudibleId,
            BridgeIdKeys.GoodreadsId,
            BridgeIdKeys.MusicBrainzId,
            BridgeIdKeys.ComicVineId,
        };

        var bridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cvEntry in canonicalValues)
        {
            if (bridgeKeys.Contains(cvEntry.Key) && !string.IsNullOrWhiteSpace(cvEntry.Value))
                bridgeIds[cvEntry.Key] = cvEntry.Value;
        }

        if (!string.IsNullOrWhiteSpace(projection?.WikidataQid) && !bridgeIds.ContainsKey(BridgeIdKeys.WikidataQid))
            bridgeIds[BridgeIdKeys.WikidataQid] = projection.WikidataQid;

        if (projection is null && canonicalValues.Count == 0)
            return Task.FromResult<RegistryItemDetail?>(null);

        Guid? reviewItemId = rqRow == default ? null : Guid.Parse(rqRow.Id);
        var detail = new RegistryItemDetail
        {
            EntityId = entityId,
            Title = projection?.Title ?? Canonical("title") ?? "Untitled",
            Year = projection?.Year ?? Canonical("release_year"),
            MediaType = projection?.MediaType ?? lineageRow.MediaType ?? "",
            CoverUrl = projection?.CoverUrl,
            HeroUrl = projection?.HeroUrl,
            Confidence = projection?.Confidence ?? (rqRow == default ? 0.0 : rqRow.ConfidenceScore ?? 0.0),
            Status = projection?.Status ?? "Confirmed",
            MatchSource = projection?.MatchSource,
            MatchMethod = projection?.PipelineStep,
            Author = projection?.Author ?? Canonical("author"),
            Director = projection?.Director ?? Canonical("director"),
            Cast = Canonical("cast"),
            Language = Canonical("language"),
            Genre = projection?.Genre ?? Canonical("genre"),
            Runtime = projection?.Runtime ?? Canonical("runtime"),
            Description = projection?.Description ?? Canonical("description"),
            Series = projection?.Series ?? Canonical("series"),
            SeriesPosition = projection?.SeriesPosition ?? Canonical("series_position"),
            Narrator = projection?.Narrator ?? Canonical("narrator"),
            Rating = projection?.Rating ?? Canonical("rating"),
            WikidataQid = projection?.WikidataQid ?? Canonical(BridgeIdKeys.WikidataQid),
            WikidataStatus = projection?.WikidataStatus,
            FileName = projection?.FileName ?? (maRow == default ? null : Path.GetFileName(maRow.FilePath)),
            FilePath = projection?.FilePath ?? maRow.FilePath,
            ContentHash = maRow.ContentHash,
            ReviewItemId = reviewItemId,
            ReviewTrigger = projection?.ReviewTrigger ?? (rqRow == default ? null : rqRow.Trigger),
            ReviewDetail = rqRow == default ? null : rqRow.Detail,
            CandidatesJson = projection?.CandidatesJson ?? (rqRow == default ? null : rqRow.CandidatesJson),
            HasUserLocks = projection?.HasUserLocks ?? claims.Any(c => c.IsUserLocked),
            MatchLevel = matchLevel,
            CanonicalValues = canonicalValues,
            ClaimHistory = claims,
            BridgeIds = bridgeIds,
            PipelineStep = projection?.PipelineStep ?? "Retail",
            LibraryVisibility = projection?.LibraryVisibility ?? "hidden",
            IsReadyForLibrary = projection?.IsReadyForLibrary ?? false,
            ArtworkState = projection?.ArtworkState ?? "pending",
            ArtworkSource = projection?.ArtworkSource,
            ArtworkSettledAt = ParseDateTimeOffset(projection?.ArtworkSettledAt),
        };

        return Task.FromResult<RegistryItemDetail?>(detail);
    }

    public Task<RegistryStatusCounts> GetStatusCountsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var row = conn.QueryFirst<StatusCountsRow>(BuildProjectionSql() + """
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN fd.status = 'InReview' OR fd.library_visibility = 'review_only' THEN 1 ELSE 0 END) AS NeedsReview,
                SUM(CASE WHEN fd.status IN ('Identified', 'Confirmed', 'RetailMatched', 'QidNoMatch', 'Edited') AND fd.library_visibility = 'visible' THEN 1 ELSE 0 END) AS AutoApproved,
                SUM(CASE WHEN fd.status = 'Edited' THEN 1 ELSE 0 END) AS Edited,
                SUM(CASE WHEN fd.has_duplicate = 1 THEN 1 ELSE 0 END) AS Duplicate,
                SUM(CASE WHEN fd.library_visibility = 'hidden' THEN 1 ELSE 0 END) AS Staging,
                SUM(CASE WHEN fd.artwork_state != 'present' THEN 1 ELSE 0 END) AS MissingImages,
                SUM(CASE WHEN fd.artwork_settled_at >= datetime('now', '-24 hours') THEN 1 ELSE 0 END) AS RecentlyUpdated,
                SUM(CASE WHEN fd.confidence BETWEEN 0.40 AND 0.85 AND fd.library_visibility != 'review_only' THEN 1 ELSE 0 END) AS LowConfidence,
                SUM(CASE WHEN fd.status = 'Rejected' THEN 1 ELSE 0 END) AS Rejected
            FROM full_data fd;
            """);

        return Task.FromResult(new RegistryStatusCounts(
            row.Total,
            row.NeedsReview,
            row.AutoApproved,
            row.Edited,
            row.Duplicate,
            row.Staging,
            row.MissingImages,
            row.RecentlyUpdated,
            row.LowConfidence,
            row.Rejected));
    }

    public Task<RegistryProjectionSummary> GetProjectionSummaryAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var staleThreshold = DateTimeOffset.UtcNow.AddDays(-30).ToString("o");
        var row = conn.QueryFirst<ProjectionSummaryRow>(BuildProjectionSql() + """
            SELECT
                COUNT(*) AS TotalItems,
                SUM(CASE WHEN fd.wikidata_qid IS NOT NULL AND fd.wikidata_qid != '' AND fd.wikidata_qid NOT LIKE 'NF%' THEN 1 ELSE 0 END) AS WithQid,
                SUM(CASE WHEN fd.wikidata_qid IS NULL OR fd.wikidata_qid = '' OR fd.wikidata_qid LIKE 'NF%' THEN 1 ELSE 0 END) AS WithoutQid,
                SUM(CASE WHEN fd.pipeline_step = 'Enrichment' THEN 1 ELSE 0 END) AS EnrichedStage3,
                SUM(CASE WHEN fd.pipeline_step != 'Enrichment' THEN 1 ELSE 0 END) AS NotEnrichedStage3,
                SUM(CASE WHEN fd.universe_assigned = 1 THEN 1 ELSE 0 END) AS UniverseAssigned,
                SUM(CASE WHEN fd.universe_assigned = 0 THEN 1 ELSE 0 END) AS UniverseUnassigned,
                SUM(CASE WHEN fd.artwork_settled_at IS NOT NULL
                           AND fd.artwork_settled_at < @staleThreshold
                           AND fd.wikidata_qid IS NOT NULL
                           AND fd.wikidata_qid != ''
                           AND fd.wikidata_qid NOT LIKE 'NF%'
                         THEN 1 ELSE 0 END) AS StaleItems,
                SUM(CASE WHEN fd.library_visibility = 'hidden' THEN 1 ELSE 0 END) AS HiddenByQualityGate,
                SUM(CASE WHEN fd.artwork_state = 'pending' THEN 1 ELSE 0 END) AS ArtPending,
                SUM(CASE WHEN fd.library_visibility = 'review_only' AND fd.pipeline_step = 'Retail' THEN 1 ELSE 0 END) AS RetailNeedsReview,
                SUM(CASE WHEN fd.status = 'QidNoMatch' THEN 1 ELSE 0 END) AS QidNoMatch,
                SUM(CASE WHEN fd.library_visibility = 'visible' AND fd.artwork_state = 'present' THEN 1 ELSE 0 END) AS CompletedWithArt
            FROM full_data fd;
            """, new { staleThreshold });

        return Task.FromResult(new RegistryProjectionSummary(
            row.TotalItems,
            row.WithQid,
            row.WithoutQid,
            row.EnrichedStage3,
            row.NotEnrichedStage3,
            row.UniverseAssigned,
            row.UniverseUnassigned,
            row.StaleItems,
            row.HiddenByQualityGate,
            row.ArtPending,
            row.RetailNeedsReview,
            row.QidNoMatch,
            row.CompletedWithArt));
    }

    public Task<RegistryFourStateCounts> GetFourStateCountsAsync(Guid? batchId = null, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        if (batchId.HasValue)
        {
            var batch = conn.QueryFirstOrDefault<(int FilesIdentified, int FilesReview, int FilesNoMatch, int FilesFailed)>("""
                SELECT files_registered AS FilesIdentified,
                       files_review AS FilesReview,
                       files_no_match AS FilesNoMatch,
                       files_failed AS FilesFailed
                FROM ingestion_batches
                WHERE id = @batchId;
                """, new { batchId = batchId.Value.ToString() });

            var batchTriggers = conn.Query<(string Trigger, int Count)>("""
                SELECT rq.trigger AS Trigger, COUNT(*) AS Count
                FROM review_queue rq
                INNER JOIN media_assets ma ON ma.id = rq.entity_id
                INNER JOIN ingestion_log il ON il.content_hash = ma.content_hash
                WHERE rq.status = 'Pending'
                  AND il.ingestion_run_id = @batchId
                GROUP BY rq.trigger
                ORDER BY Count DESC;
                """, new { batchId = batchId.Value.ToString() })
                .ToDictionary(r => r.Trigger, r => r.Count);

            return Task.FromResult(new RegistryFourStateCounts(
                batch.FilesIdentified,
                batch.FilesReview + batch.FilesNoMatch + batch.FilesFailed,
                0,
                0,
                0,
                0,
                batchTriggers));
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = BuildProjectionSql() + """
            SELECT
                COALESCE(SUM(CASE
                        WHEN fd.status IN ('Identified', 'Confirmed', 'RetailMatched', 'QidNoMatch', 'Edited')
                             AND fd.library_visibility = 'visible'
                            THEN 1
                        ELSE 0
                    END), 0) AS Identified,
                COALESCE(SUM(CASE WHEN fd.status = 'InReview' THEN 1 ELSE 0 END), 0) AS InReview,
                COALESCE(SUM(CASE WHEN fd.status = 'Provisional' THEN 1 ELSE 0 END), 0) AS Provisional,
                COALESCE(SUM(CASE WHEN fd.status = 'Rejected' THEN 1 ELSE 0 END), 0) AS Rejected,
                (SELECT COUNT(*) FROM persons) AS PersonCount,
                (SELECT COUNT(DISTINCT id) FROM collections) AS CollectionCount
            FROM full_data fd;
            """;

        int identified = 0, inReview = 0, provisional = 0, rejected = 0, personCount = 0, collectionCount = 0;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                identified = reader.GetInt32(0);
                inReview = reader.GetInt32(1);
                provisional = reader.GetInt32(2);
                rejected = reader.GetInt32(3);
                personCount = reader.GetInt32(4);
                collectionCount = reader.GetInt32(5);
            }
        }

        var triggerCounts = conn.Query<(string Trigger, int Count)>(BuildProjectionSql() + """
            SELECT fd.review_trigger AS Trigger, COUNT(*) AS Count
            FROM full_data fd
            WHERE fd.status = 'InReview'
              AND fd.review_trigger IS NOT NULL
              AND fd.review_trigger != ''
            GROUP BY fd.review_trigger
            ORDER BY Count DESC;
            """)
            .ToDictionary(r => r.Trigger, r => r.Count);

        return Task.FromResult(new RegistryFourStateCounts(
            identified, inReview, provisional, rejected, personCount, collectionCount, triggerCounts));
    }

    public Task<Dictionary<string, int>> GetMediaTypeCountsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<(string MediaType, int Count)>(BuildProjectionSql() + """
            SELECT fd.media_type AS MediaType, COUNT(*) AS Count
            FROM full_data fd
            WHERE fd.media_type IS NOT NULL AND fd.media_type != ''
              AND fd.status NOT IN ('Rejected', 'Provisional', 'InReview')
              AND fd.library_visibility = 'visible'
            GROUP BY fd.media_type;
            """);
        return Task.FromResult(rows.ToDictionary(r => r.MediaType, r => r.Count));
    }

    private static string BuildProjectionSql()
    {
        var wikidataId = WellKnownProviders.Wikidata.ToString();
        var localProcessorId = WellKnownProviders.LocalProcessor.ToString();
        var libraryScanId = WellKnownProviders.LibraryScanner.ToString();

        var sql = $"""
            WITH primary_asset_data AS (
                SELECT
                    e.work_id,
                    MIN(ma.id) AS asset_id,
                    MIN(ma.file_path_root) AS file_path_root,
                    MIN(ma.status) AS asset_status
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                GROUP BY e.work_id
            ),
            root_work_data AS (
                SELECT
                    w.id AS work_id,
                    COALESCE(gp.id, p.id, w.id) AS root_work_id
                FROM works w
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
            ),
            asset_cv AS (
                SELECT
                    pad.work_id,
                    cv.key,
                    cv.value,
                    cv.winning_provider_id,
                    cv.is_conflicted,
                    cv.last_scored_at
                FROM primary_asset_data pad
                INNER JOIN canonical_values cv ON cv.entity_id = pad.asset_id
            ),
            work_cv AS (
                SELECT
                    rwd.work_id,
                    cv.key,
                    cv.value,
                    cv.last_scored_at
                FROM root_work_data rwd
                INNER JOIN canonical_values cv ON cv.entity_id = rwd.root_work_id
            ),
            work_data AS (
                SELECT
                    w.id AS entity_id,
                    w.media_type,
                    w.wikidata_status,
                    w.curator_state,
                    CASE WHEN w.collection_id IS NOT NULL THEN 1 ELSE 0 END AS universe_assigned,
                    MAX(CASE WHEN acv.key = 'title' THEN acv.value END) AS title,
                    MAX(CASE WHEN acv.key = 'series_position' THEN acv.value END) AS series_position,
                    MAX(CASE WHEN acv.key = 'rating' THEN acv.value END) AS rating,
                    MAX(CASE WHEN acv.key = 'track_number' THEN acv.value END) AS track_number,
                    MAX(CASE WHEN acv.key = 'season_number' THEN acv.value END) AS season_number,
                    MAX(CASE WHEN acv.key = 'episode_number' THEN acv.value END) AS episode_number,
                    MAX(CASE WHEN acv.key = 'episode_title' THEN acv.value END) AS episode_title,
                    MAX(CASE WHEN acv.key = 'duration' THEN acv.value END) AS duration,
                    MAX(CASE WHEN acv.key = 'file_name' THEN acv.value END) AS file_name,
                    MAX(CASE WHEN acv.key = 'language' THEN acv.value END) AS language,
                    MAX(CASE WHEN acv.key = 'title' THEN acv.winning_provider_id END) AS title_provider_id,
                    MAX(CASE WHEN acv.key = 'wikidata_qid' THEN acv.value END) AS wikidata_qid,
                    MAX(CASE WHEN acv.key = 'qid_resolution_method' THEN acv.value END) AS qid_resolution_method,
                    MAX(CASE WHEN acv.key = 'cover_state' THEN acv.value END) AS self_cover_state,
                    MAX(CASE WHEN acv.key = 'cover_source' THEN acv.value END) AS self_cover_source,
                    MAX(CASE WHEN acv.key = 'hero_state' THEN acv.value END) AS self_hero_state,
                    MAX(CASE WHEN acv.key = 'artwork_settled_at' THEN acv.value END) AS self_artwork_settled_at,
                    MAX(CASE WHEN acv.key = 'cover_url' THEN acv.value END) AS self_cover_url,
                    MAX(CASE WHEN acv.key = 'cover_url' THEN acv.last_scored_at END) AS self_cover_last_scored_at,
                    MAX(CASE WHEN acv.key = 'hero' THEN acv.value END) AS self_hero_url,
                    MAX(CASE WHEN wcv.key IN ('release_year', 'date', 'year') THEN wcv.value END) AS year,
                    COALESCE(
                        (
                            SELECT CASE
                                WHEN cnt.total = 1 THEN a1.value
                                WHEN cnt.total = 2 THEN a1.value || ' & ' || a2.value
                                ELSE a1.value || ' & ' || a2.value || ' + ' || (cnt.total - 2) || ' more'
                            END
                            FROM (
                                SELECT COUNT(DISTINCT cva0.value) AS total
                                FROM canonical_value_arrays cva0
                                LEFT JOIN works pw0 ON pw0.id = w.parent_work_id
                                LEFT JOIN works gpw0 ON gpw0.id = pw0.parent_work_id
                                WHERE cva0.entity_id = COALESCE(gpw0.id, pw0.id, w.id)
                                  AND cva0.key = 'author'
                            ) cnt
                            LEFT JOIN (
                                SELECT DISTINCT cva1.value
                                FROM canonical_value_arrays cva1
                                LEFT JOIN works pw1 ON pw1.id = w.parent_work_id
                                LEFT JOIN works gpw1 ON gpw1.id = pw1.parent_work_id
                                WHERE cva1.entity_id = COALESCE(gpw1.id, pw1.id, w.id)
                                  AND cva1.key = 'author'
                                ORDER BY cva1.ordinal
                                LIMIT 1
                            ) a1 ON 1 = 1
                            LEFT JOIN (
                                SELECT DISTINCT cva2.value
                                FROM canonical_value_arrays cva2
                                LEFT JOIN works pw2 ON pw2.id = w.parent_work_id
                                LEFT JOIN works gpw2 ON gpw2.id = pw2.parent_work_id
                                WHERE cva2.entity_id = COALESCE(gpw2.id, pw2.id, w.id)
                                  AND cva2.key = 'author'
                                ORDER BY cva2.ordinal
                                LIMIT 1 OFFSET 1
                            ) a2 ON cnt.total >= 2
                            WHERE cnt.total >= 1
                        ),
                        MAX(CASE WHEN wcv.key = 'author' THEN wcv.value END)
                    ) AS author,
                    MAX(CASE WHEN wcv.key = 'artist' THEN wcv.value END) AS artist,
                    MAX(CASE WHEN wcv.key = 'series' THEN wcv.value END) AS series,
                    MAX(CASE WHEN wcv.key = 'narrator' THEN wcv.value END) AS narrator,
                    MAX(CASE WHEN wcv.key = 'genre' THEN wcv.value END) AS genre,
                    MAX(CASE WHEN wcv.key = 'album' THEN wcv.value END) AS album,
                    MAX(CASE WHEN wcv.key = 'show_name' THEN wcv.value END) AS show_name,
                    MAX(CASE WHEN wcv.key = 'network' THEN wcv.value END) AS network,
                    MAX(CASE WHEN wcv.key = 'description' THEN wcv.value END) AS description,
                    MAX(CASE WHEN wcv.key = 'cover_state' THEN wcv.value END) AS parent_cover_state,
                    MAX(CASE WHEN wcv.key = 'cover_source' THEN wcv.value END) AS parent_cover_source,
                    MAX(CASE WHEN wcv.key = 'hero_state' THEN wcv.value END) AS parent_hero_state,
                    MAX(CASE WHEN wcv.key = 'artwork_settled_at' THEN wcv.value END) AS parent_artwork_settled_at,
                    MAX(CASE WHEN wcv.key = 'cover_url' THEN wcv.value END) AS parent_cover_url,
                    MAX(CASE WHEN wcv.key = 'cover_url' THEN wcv.last_scored_at END) AS parent_cover_last_scored_at,
                    MAX(CASE WHEN wcv.key = 'hero' THEN wcv.value END) AS parent_hero_url,
                    (
                        SELECT GROUP_CONCAT(sub.value, ', ')
                        FROM (
                            SELECT cva.value
                            FROM canonical_value_arrays cva
                            LEFT JOIN works pw ON pw.id = w.parent_work_id
                            LEFT JOIN works gpw ON gpw.id = pw.parent_work_id
                            WHERE cva.entity_id = COALESCE(gpw.id, pw.id, w.id)
                              AND cva.key = 'cast_member'
                            ORDER BY cva.ordinal
                            LIMIT 3
                        ) sub
                    ) AS top_cast,
                    CASE
                        WHEN w.media_type = 'Movies' THEN MAX(CASE WHEN wcv.key = 'director' THEN wcv.value END)
                        ELSE MAX(CASE WHEN acv.key = 'director' THEN acv.value END)
                    END AS director,
                    CASE
                        WHEN w.media_type = 'Movies' THEN MAX(CASE WHEN wcv.key = 'runtime' THEN wcv.value END)
                        ELSE MAX(CASE WHEN acv.key = 'runtime' THEN acv.value END)
                    END AS runtime,
                    (
                        SELECT pr.name || ': ' || mc_rt.claim_value
                        FROM metadata_claims mc_rt
                        INNER JOIN media_assets ma_rt ON ma_rt.id = mc_rt.entity_id
                        INNER JOIN editions e_rt ON e_rt.id = ma_rt.edition_id
                        INNER JOIN provider_registry pr ON pr.id = mc_rt.provider_id
                        WHERE e_rt.work_id = w.id
                          AND mc_rt.claim_key IN ('title', 'episode_title', 'show_name', 'album')
                          AND mc_rt.provider_id != '{wikidataId}'
                          AND mc_rt.provider_id != 'local_filesystem'
                          AND mc_rt.provider_id != '{localProcessorId}'
                          AND mc_rt.provider_id != '{libraryScanId}'
                        ORDER BY mc_rt.confidence DESC
                        LIMIT 1
                    ) AS retail_match_detail
                FROM works w
                LEFT JOIN asset_cv acv ON acv.work_id = w.id
                LEFT JOIN work_cv wcv ON wcv.work_id = w.id
                WHERE w.work_kind != 'parent'
                GROUP BY w.id, w.media_type, w.wikidata_status, w.curator_state, w.collection_id
            )
            """;

        sql += """
            ,
            ingest_date_data AS (
                SELECT
                    e.work_id,
                    MIN(mc.claimed_at) AS first_claimed_at
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                INNER JOIN metadata_claims mc ON mc.entity_id = ma.id
                GROUP BY e.work_id
            ),
            review_data AS (
                SELECT
                    e_rv.work_id AS entity_id,
                    (
                        SELECT rq2.id
                        FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id
                          AND rq2.status = 'Pending'
                        ORDER BY rq2.created_at DESC
                        LIMIT 1
                    ) AS review_id,
                    (
                        SELECT rq2.trigger
                        FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id
                          AND rq2.status = 'Pending'
                        ORDER BY rq2.created_at DESC
                        LIMIT 1
                    ) AS review_trigger,
                    (
                        SELECT MAX(rq2.confidence_score)
                        FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id
                          AND rq2.status = 'Pending'
                    ) AS review_confidence,
                    (
                        SELECT rq2.candidates_json
                        FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id
                          AND rq2.status = 'Pending'
                          AND rq2.candidates_json IS NOT NULL
                        ORDER BY rq2.created_at DESC
                        LIMIT 1
                    ) AS candidates_json
                FROM review_queue rq
                INNER JOIN media_assets ma_rv ON ma_rv.id = rq.entity_id
                INNER JOIN editions e_rv ON e_rv.id = ma_rv.edition_id
                WHERE rq.status = 'Pending'
                GROUP BY e_rv.work_id
            ),
            identity_job_data AS (
                SELECT
                    e_ij.work_id AS entity_id,
                    (
                        SELECT ij2.state
                        FROM identity_jobs ij2
                        INNER JOIN media_assets ma_ij2 ON ma_ij2.id = ij2.entity_id
                        INNER JOIN editions e_ij2 ON e_ij2.id = ma_ij2.edition_id
                        WHERE e_ij2.work_id = e_ij.work_id
                        ORDER BY ij2.updated_at DESC, ij2.created_at DESC
                        LIMIT 1
                    ) AS job_state,
                    (
                        SELECT ij2.updated_at
                        FROM identity_jobs ij2
                        INNER JOIN media_assets ma_ij2 ON ma_ij2.id = ij2.entity_id
                        INNER JOIN editions e_ij2 ON e_ij2.id = ma_ij2.edition_id
                        WHERE e_ij2.work_id = e_ij.work_id
                        ORDER BY ij2.updated_at DESC, ij2.created_at DESC
                        LIMIT 1
                    ) AS job_updated_at,
                    (
                        SELECT ij2.resolved_qid
                        FROM identity_jobs ij2
                        INNER JOIN media_assets ma_ij2 ON ma_ij2.id = ij2.entity_id
                        INNER JOIN editions e_ij2 ON e_ij2.id = ma_ij2.edition_id
                        WHERE e_ij2.work_id = e_ij.work_id
                        ORDER BY ij2.updated_at DESC, ij2.created_at DESC
                        LIMIT 1
                    ) AS resolved_qid
                FROM identity_jobs ij
                INNER JOIN media_assets ma_ij ON ma_ij.id = ij.entity_id
                INNER JOIN editions e_ij ON e_ij.id = ma_ij.edition_id
                GROUP BY e_ij.work_id
            ),
            user_lock_data AS (
                SELECT
                    pad.work_id AS entity_id,
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM metadata_claims mc
                        WHERE mc.entity_id = pad.asset_id
                          AND mc.is_user_locked = 1
                    ) THEN 1 ELSE 0 END AS has_locks
                FROM primary_asset_data pad
            ),
            raw_data AS (
                SELECT
                    wd.entity_id,
                    COALESCE(NULLIF(wd.title, ''), 'Untitled') AS title,
                    wd.year,
                    wd.media_type,
                    wd.author,
                    wd.director,
                    wd.artist,
                    wd.series,
                    wd.series_position,
                    wd.narrator,
                    wd.genre,
                    wd.runtime,
                    wd.rating,
                    wd.album,
                    wd.track_number,
                    wd.season_number,
                    wd.episode_number,
                    wd.show_name,
                    wd.episode_title,
                    wd.network,
                    wd.top_cast,
                    wd.duration,
                    wd.description,
                    wd.language,
                    wd.file_name,
                    wd.title_provider_id AS match_source,
                    wd.retail_match_detail,
                    COALESCE(NULLIF(wd.wikidata_qid, ''), NULLIF(ij.resolved_qid, '')) AS wikidata_qid,
                    wd.qid_resolution_method,
                    wd.wikidata_status,
                    wd.curator_state,
                    wd.universe_assigned,
                    pad.asset_id,
                    pad.file_path_root AS file_path,
                    pad.asset_status,
                    rd.review_id,
                    rd.review_trigger,
                    rd.review_confidence,
                    rd.candidates_json,
                    COALESCE(ul.has_locks, 0) AS has_user_locks,
                    ij.job_state,
                    ij.job_updated_at,
                    idd.first_claimed_at AS created_at,
                    COALESCE(NULLIF(wd.self_cover_state, ''), NULLIF(wd.parent_cover_state, ''), CASE
                        WHEN COALESCE(NULLIF(wd.self_cover_url, ''), NULLIF(wd.parent_cover_url, '')) IS NOT NULL THEN 'present'
                        ELSE 'pending'
                    END) AS artwork_state,
                    COALESCE(NULLIF(wd.self_cover_source, ''), NULLIF(wd.parent_cover_source, ''), CASE
                        WHEN COALESCE(NULLIF(wd.self_cover_url, ''), NULLIF(wd.parent_cover_url, '')) LIKE 'http%' THEN 'provider'
                        WHEN COALESCE(NULLIF(wd.self_cover_url, ''), NULLIF(wd.parent_cover_url, '')) LIKE '/stream/%' THEN 'embedded'
                        ELSE NULL
                    END) AS artwork_source,
                    COALESCE(NULLIF(wd.self_artwork_settled_at, ''), NULLIF(wd.parent_artwork_settled_at, ''), CASE
                        WHEN COALESCE(NULLIF(wd.self_cover_url, ''), NULLIF(wd.parent_cover_url, '')) IS NOT NULL
                            THEN COALESCE(wd.self_cover_last_scored_at, wd.parent_cover_last_scored_at, ij.job_updated_at)
                        ELSE NULL
                    END) AS artwork_settled_at,
                    COALESCE(NULLIF(wd.self_hero_state, ''), NULLIF(wd.parent_hero_state, ''), CASE
                        WHEN COALESCE(NULLIF(wd.self_hero_url, ''), NULLIF(wd.parent_hero_url, '')) IS NOT NULL THEN 'present'
                        ELSE 'pending'
                    END) AS hero_state,
                    CASE
                        WHEN COALESCE(NULLIF(wd.wikidata_qid, ''), NULLIF(ij.resolved_qid, '')) IS NOT NULL
                             AND COALESCE(NULLIF(wd.wikidata_qid, ''), NULLIF(ij.resolved_qid, '')) NOT LIKE 'NF%'
                            THEN 1 ELSE 0
                    END AS has_valid_qid,
                    CASE WHEN COALESCE(NULLIF(wd.title, ''), '') NOT IN ('', 'Untitled', 'Unknown') THEN 1 ELSE 0 END AS has_quality_title,
                    CASE WHEN COALESCE(NULLIF(wd.media_type, ''), '') NOT IN ('', 'Unknown') THEN 1 ELSE 0 END AS has_resolved_media_type
                FROM work_data wd
                LEFT JOIN primary_asset_data pad ON pad.work_id = wd.entity_id
                LEFT JOIN review_data rd ON rd.entity_id = wd.entity_id
                LEFT JOIN identity_job_data ij ON ij.entity_id = wd.entity_id
                LEFT JOIN user_lock_data ul ON ul.entity_id = wd.entity_id
                LEFT JOIN ingest_date_data idd ON idd.work_id = wd.entity_id
            )
            """;

        sql += """
            ,
            full_data AS (
                SELECT
                    rd.entity_id,
                    rd.title,
                    rd.year,
                    rd.media_type,
                    CASE
                        WHEN rd.artwork_state = 'present' AND rd.asset_id IS NOT NULL THEN '/stream/' || rd.asset_id || '/cover'
                        ELSE NULL
                    END AS cover_url,
                    CASE
                        WHEN rd.hero_state = 'present' AND rd.asset_id IS NOT NULL THEN '/stream/' || rd.asset_id || '/hero'
                        ELSE NULL
                    END AS hero_url,
                    rd.author,
                    rd.director,
                    rd.artist,
                    rd.series,
                    rd.series_position,
                    rd.narrator,
                    rd.genre,
                    rd.runtime,
                    rd.rating,
                    rd.album,
                    rd.track_number,
                    rd.season_number,
                    rd.episode_number,
                    rd.show_name,
                    rd.episode_title,
                    rd.network,
                    rd.top_cast,
                    rd.duration,
                    rd.description,
                    rd.language,
                    rd.file_name,
                    rd.match_source,
                    rd.review_id,
                    rd.review_trigger,
                    rd.review_confidence,
                    rd.candidates_json,
                    rd.has_user_locks,
                    rd.file_path,
                    rd.wikidata_status,
                    rd.wikidata_qid,
                    rd.qid_resolution_method,
                    rd.retail_match_detail,
                    rd.artwork_state,
                    rd.artwork_source,
                    rd.artwork_settled_at,
                    rd.universe_assigned,
                    rd.created_at,
                    rd.asset_status,
                    CASE
                        WHEN rd.has_quality_title = 1
                             AND rd.has_resolved_media_type = 1
                             AND (
                                 rd.artwork_state = 'present'
                                 OR (rd.artwork_state = 'missing' AND rd.artwork_settled_at IS NOT NULL)
                             )
                            THEN 1 ELSE 0
                    END AS is_ready_for_library,
                    CASE
                        WHEN rd.curator_state IN ('rejected', 'provisional')
                             OR rd.job_state = 'QidNeedsReview'
                             OR (
                                 rd.review_id IS NOT NULL
                                 AND rd.review_trigger != 'WritebackFailed'
                                 AND (
                                     rd.job_state IS NULL
                                     OR rd.job_state NOT IN (
                                         'Queued',
                                         'RetailSearching',
                                         'RetailMatched',
                                         'RetailMatchedNeedsReview',
                                         'BridgeSearching',
                                         'QidResolved',
                                         'Hydrating'
                                     )
                                 )
                             )
                            THEN 'review_only'
                        WHEN rd.has_quality_title = 1
                             AND rd.has_resolved_media_type = 1
                             AND (
                                 rd.artwork_state = 'present'
                                 OR (rd.artwork_state = 'missing' AND rd.artwork_settled_at IS NOT NULL)
                             )
                            THEN 'visible'
                        ELSE 'hidden'
                    END AS library_visibility,
                    CASE
                        WHEN rd.curator_state = 'rejected' THEN 'Rejected'
                        WHEN rd.curator_state = 'provisional' THEN 'Provisional'
                        WHEN rd.job_state = 'QidNeedsReview' THEN 'InReview'
                        WHEN rd.review_id IS NOT NULL
                             AND rd.review_trigger != 'WritebackFailed'
                             AND (
                                 rd.job_state IS NULL
                                 OR rd.job_state NOT IN (
                                     'Queued',
                                     'RetailSearching',
                                     'RetailMatched',
                                     'RetailMatchedNeedsReview',
                                     'BridgeSearching',
                                     'QidResolved',
                                     'Hydrating'
                                 )
                             )
                            THEN 'InReview'
                        WHEN rd.has_valid_qid = 1 THEN 'Identified'
                        WHEN rd.job_state = 'QidNoMatch' THEN 'QidNoMatch'
                        WHEN rd.job_state IN ('RetailMatched', 'BridgeSearching', 'QidResolved', 'Hydrating', 'Completed') THEN 'RetailMatched'
                        WHEN rd.has_user_locks = 1 THEN 'Edited'
                        ELSE 'Confirmed'
                    END AS status,
                    CASE WHEN rd.asset_status = 'Conflicted' THEN 1 ELSE 0 END AS has_duplicate,
                    CASE
                        WHEN rd.has_valid_qid = 1 THEN 'matched'
                        WHEN rd.job_state = 'QidNoMatch' THEN 'failed'
                        WHEN rd.review_trigger IN ('MissingQid', 'MultipleQidMatches', 'WikidataBridgeFailed')
                             OR rd.job_state = 'QidNeedsReview'
                            THEN 'warning'
                        WHEN rd.job_state IN ('BridgeSearching', 'RetailMatched', 'RetailMatchedNeedsReview', 'QidResolved', 'Hydrating')
                            THEN 'warning'
                        ELSE 'none'
                    END AS wikidata_match,
                    CASE
                        WHEN rd.job_state IN ('RetailMatched', 'RetailMatchedNeedsReview', 'BridgeSearching', 'QidResolved', 'QidNeedsReview', 'QidNoMatch', 'Hydrating', 'Completed')
                            THEN 'matched'
                        WHEN rd.job_state = 'RetailNoMatch' THEN 'failed'
                        WHEN rd.review_trigger IN ('AuthorityMatchFailed', 'RetailMatchFailed', 'ContentMatchFailed')
                            THEN 'warning'
                        ELSE 'none'
                    END AS retail_match,
                    CASE
                        WHEN rd.review_trigger IN ('AuthorityMatchFailed', 'RetailMatchFailed', 'ContentMatchFailed')
                             OR rd.job_state IN ('Queued', 'RetailSearching', 'RetailMatchedNeedsReview', 'RetailNoMatch', 'Failed')
                            THEN 'Retail'
                        WHEN rd.job_state = 'QidNoMatch'
                            THEN 'Wikidata'
                        WHEN rd.has_valid_qid = 0
                             AND rd.job_state IS NOT NULL
                             AND rd.job_state NOT IN ('Queued', 'RetailSearching', 'RetailMatchedNeedsReview', 'RetailNoMatch', 'Failed')
                            THEN 'Wikidata'
                        ELSE 'Enrichment'
                    END AS pipeline_step,
                    CASE
                        WHEN rd.review_confidence IS NOT NULL THEN rd.review_confidence
                        WHEN rd.has_valid_qid = 1 THEN 1.0
                        WHEN rd.job_state IN ('RetailMatched', 'BridgeSearching', 'QidResolved', 'Hydrating', 'Completed', 'QidNoMatch') THEN 0.85
                        WHEN rd.job_state = 'RetailMatchedNeedsReview' THEN 0.65
                        WHEN rd.job_state = 'RetailNoMatch' THEN 0.30
                        ELSE 0.0
                    END AS confidence
                FROM raw_data rd
            )
            """;

        return sql;
    }

    private static void AddParameters(SqliteCommand cmd, RegistryQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            cmd.Parameters.AddWithValue("@search", $"%{query.Search}%");
            var escaped = query.Search.Trim().Replace("\"", "\"\"");
            cmd.Parameters.AddWithValue("@ftsQuery", $"\"{escaped}\"*");
        }

        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            var types = query.MediaType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (types.Length == 1)
            {
                cmd.Parameters.AddWithValue("@mediaType", NormalizeMediaType(types[0]));
            }
            else
            {
                for (var i = 0; i < types.Length; i++)
                    cmd.Parameters.AddWithValue($"@mt{i}", NormalizeMediaType(types[i]));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && query.Status != "Approved")
            cmd.Parameters.AddWithValue("@status", query.Status);

        if (query.MinConfidence.HasValue)
            cmd.Parameters.AddWithValue("@minConfidence", query.MinConfidence.Value);

        if (!string.IsNullOrWhiteSpace(query.MatchSource))
            cmd.Parameters.AddWithValue("@matchSource", query.MatchSource);
    }

    private static string NormalizeMediaType(string raw) => raw.ToUpperInvariant() switch
    {
        "EPUB" or "BOOK" or "EBOOK" => "Books",
        "AUDIOBOOK" => "Audiobooks",
        "MOVIE" => "Movies",
        "COMIC" => "Comics",
        _ => raw,
    };

    private static DateTimeOffset? ParseDateTimeOffset(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed class ProjectionRow
    {
        public string EntityId { get; set; } = "";
        public string Title { get; set; } = "";
        public string? Year { get; set; }
        public string MediaType { get; set; } = "";
        public string? CoverUrl { get; set; }
        public string? HeroUrl { get; set; }
        public double Confidence { get; set; }
        public string Status { get; set; } = "Confirmed";
        public string? MatchSource { get; set; }
        public string? Author { get; set; }
        public string? Director { get; set; }
        public string? Genre { get; set; }
        public string? Runtime { get; set; }
        public string? Description { get; set; }
        public string? Series { get; set; }
        public string? SeriesPosition { get; set; }
        public string? Narrator { get; set; }
        public string? Rating { get; set; }
        public string? WikidataQid { get; set; }
        public string? WikidataStatus { get; set; }
        public string? FileName { get; set; }
        public string? FilePath { get; set; }
        public string? ReviewId { get; set; }
        public string? ReviewTrigger { get; set; }
        public string? CandidatesJson { get; set; }
        public bool HasUserLocks { get; set; }
        public string PipelineStep { get; set; } = "Retail";
        public string LibraryVisibility { get; set; } = "hidden";
        public bool IsReadyForLibrary { get; set; }
        public string ArtworkState { get; set; } = "pending";
        public string? ArtworkSource { get; set; }
        public string? ArtworkSettledAt { get; set; }
        public string? QidResolutionMethod { get; set; }
    }

    private sealed class StatusCountsRow
    {
        public int Total { get; set; }
        public int NeedsReview { get; set; }
        public int AutoApproved { get; set; }
        public int Edited { get; set; }
        public int Duplicate { get; set; }
        public int Staging { get; set; }
        public int MissingImages { get; set; }
        public int RecentlyUpdated { get; set; }
        public int LowConfidence { get; set; }
        public int Rejected { get; set; }
    }

    private sealed class ProjectionSummaryRow
    {
        public int TotalItems { get; set; }
        public int WithQid { get; set; }
        public int WithoutQid { get; set; }
        public int EnrichedStage3 { get; set; }
        public int NotEnrichedStage3 { get; set; }
        public int UniverseAssigned { get; set; }
        public int UniverseUnassigned { get; set; }
        public int StaleItems { get; set; }
        public int HiddenByQualityGate { get; set; }
        public int ArtPending { get; set; }
        public int RetailNeedsReview { get; set; }
        public int QidNoMatch { get; set; }
        public int CompletedWithArt { get; set; }
    }
}
