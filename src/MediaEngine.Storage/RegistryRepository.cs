using Microsoft.Data.Sqlite;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IRegistryRepository"/>.
/// Joins works, canonical_values, review_queue, metadata_claims, and media_assets
/// into a unified registry view.
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

        // Build the core query with pivoted canonical values.
        // Canonical values are stored against media_asset.id, so we join
        // through editions → media_assets to find the correct entity_id.
        var sql = """
            WITH work_data AS (
                SELECT
                    w.id AS entity_id,
                    w.media_type,
                    w.wikidata_status,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS title,
                    MAX(CASE WHEN cv.key = 'release_year' THEN cv.value END) AS year,
                    MAX(CASE WHEN cv.key = 'cover_url' THEN cv.value END) AS cover_url,
                    MAX(CASE WHEN cv.key = 'author' THEN cv.value END) AS author,
                    MAX(CASE WHEN cv.key = 'file_name' THEN cv.value END) AS file_name,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.winning_provider_id END) AS title_provider_id,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.is_conflicted END) AS title_conflicted
                FROM works w
                LEFT JOIN editions e2 ON e2.work_id = w.id
                LEFT JOIN media_assets ma2 ON ma2.edition_id = e2.id
                LEFT JOIN canonical_values cv ON cv.entity_id = ma2.id
                GROUP BY w.id, w.media_type, w.wikidata_status
            ),
            asset_data AS (
                SELECT
                    e.work_id,
                    MIN(ma.id) AS asset_id,
                    MIN(ma.file_path_root) AS file_path_root,
                    MIN(ma.status) AS asset_status,
                    MAX(LENGTH(ma.content_hash)) AS file_size_proxy
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                GROUP BY e.work_id
            ),
            review_data AS (
                SELECT rq.entity_id,
                       MIN(rq.id) AS review_id,
                       MIN(rq.trigger) AS trigger,
                       MAX(rq.confidence_score) AS confidence_score,
                       MIN(rq.candidates_json) AS candidates_json
                FROM review_queue rq
                WHERE rq.status = 'Pending'
                GROUP BY rq.entity_id
            ),
            user_lock_data AS (
                SELECT mc.entity_id, 1 AS has_locks
                FROM metadata_claims mc
                WHERE mc.is_user_locked = 1
                GROUP BY mc.entity_id
            ),
            full_data AS (
                SELECT
                    wd.entity_id,
                    COALESCE(wd.title, 'Untitled') AS title,
                    wd.year,
                    wd.media_type,
                    wd.cover_url,
                    wd.author,
                    wd.file_name,
                    wd.title_provider_id AS match_source,
                    rd.review_id,
                    rd.trigger AS review_trigger,
                    COALESCE(rd.confidence_score, 0.95) AS confidence,
                    COALESCE(ul.has_locks, 0) AS has_user_locks,
                    CASE
                        WHEN rd.review_id IS NOT NULL THEN 'Review'
                        WHEN ul.has_locks = 1 THEN 'Edited'
                        WHEN ad.asset_status = 'Conflicted' THEN 'Duplicate'
                        WHEN ad.file_path_root LIKE '%/.staging/%' OR ad.file_path_root LIKE '%\.staging\%' THEN 'Staging'
                        ELSE 'Auto'
                    END AS status,
                    CASE WHEN ad.asset_status = 'Conflicted' THEN 1 ELSE 0 END AS has_duplicate,
                    ad.file_path_root,
                    wd.wikidata_status
                FROM work_data wd
                LEFT JOIN asset_data ad ON ad.work_id = wd.entity_id
                LEFT JOIN review_data rd ON rd.entity_id = ad.asset_id
                LEFT JOIN user_lock_data ul ON ul.entity_id = ad.asset_id
            )
            """;

        // Build WHERE clause
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Search))
            conditions.Add("(fd.title LIKE @search OR fd.file_name LIKE @search OR fd.author LIKE @search)");
        if (!string.IsNullOrWhiteSpace(query.MediaType))
            conditions.Add("fd.media_type = @mediaType");
        if (!string.IsNullOrWhiteSpace(query.Status))
            conditions.Add("fd.status = @status");
        if (query.MinConfidence.HasValue)
            conditions.Add("fd.confidence >= @minConfidence");
        if (!string.IsNullOrWhiteSpace(query.MatchSource))
            conditions.Add("fd.match_source = @matchSource");
        if (query.DuplicatesOnly)
            conditions.Add("fd.has_duplicate = 1");
        if (query.MissingUniverseOnly)
            conditions.Add("fd.wikidata_status IN ('missing', 'manual')");

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        // Count query
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = sql + $"SELECT COUNT(*) FROM full_data fd {whereClause}";
        AddParameters(countCmd, query);
        var totalCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        // Data query
        using var dataCmd = conn.CreateCommand();
        dataCmd.CommandText = sql + $"""
            SELECT
                fd.entity_id, fd.title, fd.year, fd.media_type, fd.cover_url,
                fd.match_source, fd.confidence, fd.status, fd.has_duplicate,
                fd.review_id, fd.review_trigger, fd.has_user_locks,
                fd.file_name, fd.author, fd.file_path_root, fd.wikidata_status
            FROM full_data fd
            {whereClause}
            ORDER BY fd.confidence ASC, fd.title ASC
            LIMIT @limit OFFSET @offset
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
                MediaType = reader.GetString(3),
                CoverUrl = reader.IsDBNull(4) ? null : reader.GetString(4),
                MatchSource = reader.IsDBNull(5) ? null : reader.GetString(5),
                Confidence = reader.GetDouble(6),
                Status = reader.GetString(7),
                HasDuplicate = reader.GetInt32(8) == 1,
                ReviewItemId = reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
                ReviewTrigger = reader.IsDBNull(10) ? null : reader.GetString(10),
                HasUserLocks = reader.GetInt32(11) == 1,
                FileName = reader.IsDBNull(12) ? null : reader.GetString(12),
                Author = reader.IsDBNull(13) ? null : reader.GetString(13),
                WikidataStatus = reader.IsDBNull(15) ? null : reader.GetString(15),
            });
        }

        return Task.FromResult(new RegistryPageResult(items, totalCount, query.Offset + items.Count < totalCount));
    }

    public Task<RegistryItemDetail?> GetDetailAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        // Canonical values and claims are stored against media_asset.id, not works.id.
        // Resolve the asset ID through the editions → media_assets chain.
        string? assetIdStr = null;
        using (var assetCmd = conn.CreateCommand())
        {
            assetCmd.CommandText = """
                SELECT MIN(ma.id) AS id
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @entityId
                """;
            assetCmd.Parameters.AddWithValue("@entityId", entityId.ToString());
            assetIdStr = assetCmd.ExecuteScalar()?.ToString();
        }

        if (assetIdStr is null)
            return Task.FromResult<RegistryItemDetail?>(null);

        // Load all canonical values for this entity (keyed by asset ID)
        var canonicalValues = new List<RegistryCanonicalValue>();
        using (var cvCmd = conn.CreateCommand())
        {
            cvCmd.CommandText = """
                SELECT key, value, is_conflicted, winning_provider_id, needs_review, last_scored_at
                FROM canonical_values
                WHERE entity_id = @assetId
                ORDER BY key
                """;
            cvCmd.Parameters.AddWithValue("@assetId", assetIdStr);
            using var cvReader = cvCmd.ExecuteReader();
            while (cvReader.Read())
            {
                canonicalValues.Add(new RegistryCanonicalValue(
                    Key: cvReader.GetString(0),
                    Value: cvReader.GetString(1),
                    IsConflicted: cvReader.GetInt32(2) == 1,
                    WinningProviderId: cvReader.IsDBNull(3) ? null : cvReader.GetString(3),
                    NeedsReview: cvReader.GetInt32(4) == 1,
                    LastScoredAt: DateTimeOffset.TryParse(cvReader.GetString(5), out var dt) ? dt : DateTimeOffset.MinValue));
            }
        }

        if (canonicalValues.Count == 0)
            return Task.FromResult<RegistryItemDetail?>(null);

        // Load claim history (also keyed by asset ID)
        var claims = new List<RegistryClaimRecord>();
        using (var claimCmd = conn.CreateCommand())
        {
            claimCmd.CommandText = """
                SELECT id, claim_key, claim_value, provider_id, confidence, is_user_locked, claimed_at
                FROM metadata_claims
                WHERE entity_id = @assetId
                ORDER BY claimed_at DESC
                """;
            claimCmd.Parameters.AddWithValue("@assetId", assetIdStr);
            using var claimReader = claimCmd.ExecuteReader();
            while (claimReader.Read())
            {
                claims.Add(new RegistryClaimRecord(
                    Id: Guid.Parse(claimReader.GetString(0)),
                    ClaimKey: claimReader.GetString(1),
                    ClaimValue: claimReader.GetString(2),
                    ProviderId: Guid.Parse(claimReader.GetString(3)),
                    Confidence: claimReader.GetDouble(4),
                    IsUserLocked: claimReader.GetInt32(5) == 1,
                    ClaimedAt: DateTimeOffset.TryParse(claimReader.GetString(6), out var cdt) ? cdt : DateTimeOffset.MinValue));
            }
        }

        // Load review queue entry if any (review_queue uses asset ID as entity_id)
        Guid? reviewItemId = null;
        string? reviewTrigger = null, reviewDetail = null, candidatesJson = null;
        double reviewConfidence = 0.95;
        using (var rqCmd = conn.CreateCommand())
        {
            rqCmd.CommandText = """
                SELECT id, trigger, confidence_score, detail, candidates_json
                FROM review_queue
                WHERE (entity_id = @assetId OR entity_id = @entityId) AND status = 'Pending'
                LIMIT 1
                """;
            rqCmd.Parameters.AddWithValue("@assetId", assetIdStr);
            rqCmd.Parameters.AddWithValue("@entityId", entityId.ToString());
            using var rqReader = rqCmd.ExecuteReader();
            if (rqReader.Read())
            {
                reviewItemId = Guid.Parse(rqReader.GetString(0));
                reviewTrigger = rqReader.GetString(1);
                reviewConfidence = rqReader.IsDBNull(2) ? 0.5 : rqReader.GetDouble(2);
                reviewDetail = rqReader.IsDBNull(3) ? null : rqReader.GetString(3);
                candidatesJson = rqReader.IsDBNull(4) ? null : rqReader.GetString(4);
            }
        }

        // Load media asset info
        string? filePath = null, contentHash = null, fileName = null;
        using (var maCmd = conn.CreateCommand())
        {
            maCmd.CommandText = """
                SELECT ma.file_path_root, ma.content_hash
                FROM editions e
                INNER JOIN media_assets ma ON ma.edition_id = e.id
                WHERE e.work_id = @entityId
                LIMIT 1
                """;
            maCmd.Parameters.AddWithValue("@entityId", entityId.ToString());
            using var maReader = maCmd.ExecuteReader();
            if (maReader.Read())
            {
                filePath = maReader.IsDBNull(0) ? null : maReader.GetString(0);
                contentHash = maReader.IsDBNull(1) ? null : maReader.GetString(1);
                if (filePath is not null)
                    fileName = Path.GetFileName(filePath);
            }
        }

        // Load wikidata_status from the work
        string? wikidataStatus = null;
        using (var wsCmd = conn.CreateCommand())
        {
            wsCmd.CommandText = "SELECT wikidata_status FROM works WHERE id = @entityId";
            wsCmd.Parameters.AddWithValue("@entityId", entityId.ToString());
            wikidataStatus = wsCmd.ExecuteScalar()?.ToString();
        }

        // Load work media type
        string mediaType = "";
        using (var wCmd = conn.CreateCommand())
        {
            wCmd.CommandText = "SELECT media_type FROM works WHERE id = @entityId";
            wCmd.Parameters.AddWithValue("@entityId", entityId.ToString());
            mediaType = wCmd.ExecuteScalar()?.ToString() ?? "";
        }

        // Helper to get canonical value by key
        string? cv(string key) => canonicalValues.FirstOrDefault(v => v.Key == key)?.Value;
        var hasUserLocks = claims.Any(c => c.IsUserLocked);
        var titleProvider = canonicalValues.FirstOrDefault(v => v.Key == "title")?.WinningProviderId;

        var status = reviewItemId.HasValue ? "Review"
            : hasUserLocks ? "Edited"
            : "Auto";

        // Collect bridge identifiers from canonical values
        var bridgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "isbn", "isbn_13", "isbn_10", "asin", "tmdb_id", "imdb_id", "wikidata_qid",
            "apple_books_id", "audible_id", "goodreads_id", "musicbrainz_id",
            "comic_vine_id"
        };
        var bridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cvEntry in canonicalValues)
        {
            if (bridgeKeys.Contains(cvEntry.Key) && !string.IsNullOrWhiteSpace(cvEntry.Value))
                bridgeIds[cvEntry.Key] = cvEntry.Value;
        }
        var wikidataQid = cv("wikidata_qid");
        if (!string.IsNullOrEmpty(wikidataQid) && !bridgeIds.ContainsKey("wikidata_qid"))
            bridgeIds["wikidata_qid"] = wikidataQid;

        var detail = new RegistryItemDetail
        {
            EntityId = entityId,
            Title = cv("title") ?? "Untitled",
            Year = cv("release_year"),
            MediaType = mediaType,
            CoverUrl = cv("cover_url"),
            Confidence = reviewConfidence,
            Status = status,
            MatchSource = titleProvider,
            Author = cv("author"),
            Director = cv("director"),
            Cast = cv("cast"),
            Language = cv("language"),
            Genre = cv("genre"),
            Runtime = cv("runtime"),
            Description = cv("description"),
            Series = cv("series"),
            SeriesPosition = cv("series_position"),
            Narrator = cv("narrator"),
            Rating = cv("rating"),
            WikidataQid = wikidataQid,
            WikidataStatus = wikidataStatus,
            FileName = fileName ?? cv("file_name"),
            FilePath = filePath,
            ContentHash = contentHash,
            ReviewItemId = reviewItemId,
            ReviewTrigger = reviewTrigger,
            ReviewDetail = reviewDetail,
            CandidatesJson = candidatesJson,
            HasUserLocks = hasUserLocks,
            CanonicalValues = canonicalValues,
            ClaimHistory = claims,
            BridgeIds = bridgeIds,
        };

        return Task.FromResult<RegistryItemDetail?>(detail);
    }

    public Task<RegistryStatusCounts> GetStatusCountsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();

        // Review queue and metadata claims use media_asset.id as entity_id,
        // so we join through editions → media_assets to map back to works.
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM works) AS total,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM review_queue rq
                 INNER JOIN media_assets ma ON ma.id = rq.entity_id
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE rq.status = 'Pending') AS needs_review,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM metadata_claims mc
                 INNER JOIN media_assets ma ON ma.id = mc.entity_id
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE mc.is_user_locked = 1) AS edited,
                (SELECT COUNT(*) FROM media_assets WHERE status = 'Conflicted') AS duplicate,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM media_assets ma
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE (ma.file_path_root LIKE '%/.staging/%'
                    OR ma.file_path_root LIKE '%\.staging\%')
                   AND NOT EXISTS (
                     SELECT 1 FROM review_queue rq
                     WHERE rq.entity_id = ma.id AND rq.status = 'Pending'
                   )) AS staging
            """;

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var total = reader.GetInt32(0);
            var needsReview = reader.GetInt32(1);
            var edited = reader.GetInt32(2);
            var duplicate = reader.GetInt32(3);
            var staging = reader.GetInt32(4);
            var auto = total - needsReview - edited - duplicate - staging;

            return Task.FromResult(new RegistryStatusCounts(total, needsReview, Math.Max(auto, 0), edited, duplicate, staging));
        }

        return Task.FromResult(new RegistryStatusCounts(0, 0, 0, 0, 0, 0));
    }

    private static void AddParameters(SqliteCommand cmd, RegistryQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
            cmd.Parameters.AddWithValue("@search", $"%{query.Search}%");
        if (!string.IsNullOrWhiteSpace(query.MediaType))
            cmd.Parameters.AddWithValue("@mediaType", query.MediaType);
        if (!string.IsNullOrWhiteSpace(query.Status))
            cmd.Parameters.AddWithValue("@status", query.Status);
        if (query.MinConfidence.HasValue)
            cmd.Parameters.AddWithValue("@minConfidence", query.MinConfidence.Value);
        if (!string.IsNullOrWhiteSpace(query.MatchSource))
            cmd.Parameters.AddWithValue("@matchSource", query.MatchSource);
    }
}
