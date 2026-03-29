using System.Linq;
using Dapper;
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

        // Build the core CTE query with pivoted canonical values.
        // Canonical values are stored against media_asset.id, so we join
        // through editions → media_assets to find the correct entity_id.
        var sql = """
            WITH work_data AS (
                SELECT
                    w.id AS entity_id,
                    w.media_type,
                    w.wikidata_status,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.value END) AS title,
                    MAX(CASE WHEN cv.key IN ('release_year', 'date', 'year') THEN cv.value END) AS year,
                    MAX(CASE WHEN cv.key = 'cover_url' THEN cv.value END) AS cover_url,
                    MAX(CASE WHEN cv.key = 'hero' THEN cv.value END) AS hero_url,
                    -- Multi-author display: "A & B" or "A & B + N more", falling back to
                    -- canonical_values.author when no array entries exist.
                    COALESCE(
                        (SELECT
                            CASE
                                WHEN cnt.total = 1 THEN a1.value
                                WHEN cnt.total = 2 THEN a1.value || ' & ' || a2.value
                                ELSE a1.value || ' & ' || a2.value || ' + ' || (cnt.total - 2) || ' more'
                            END
                         FROM (SELECT COUNT(*) AS total
                               FROM canonical_value_arrays cva0
                               INNER JOIN media_assets ma0 ON ma0.id = cva0.entity_id
                               INNER JOIN editions e0 ON e0.id = ma0.edition_id
                               WHERE e0.work_id = w.id AND cva0.key = 'author') cnt
                         LEFT JOIN (SELECT cva1.value, cva1.entity_id
                                    FROM canonical_value_arrays cva1
                                    INNER JOIN media_assets ma1 ON ma1.id = cva1.entity_id
                                    INNER JOIN editions e1 ON e1.id = ma1.edition_id
                                    WHERE e1.work_id = w.id AND cva1.key = 'author'
                                    ORDER BY cva1.ordinal LIMIT 1) a1 ON 1=1
                         LEFT JOIN (SELECT cva2.value, cva2.entity_id
                                    FROM canonical_value_arrays cva2
                                    INNER JOIN media_assets ma2b ON ma2b.id = cva2.entity_id
                                    INNER JOIN editions e2b ON e2b.id = ma2b.edition_id
                                    WHERE e2b.work_id = w.id AND cva2.key = 'author'
                                    ORDER BY cva2.ordinal LIMIT 1 OFFSET 1) a2 ON cnt.total >= 2
                         WHERE cnt.total >= 1),
                        MAX(CASE WHEN cv.key = 'author' THEN cv.value END)
                    ) AS author,
                    MAX(CASE WHEN cv.key = 'director' THEN cv.value END) AS director,
                    MAX(CASE WHEN cv.key = 'artist' THEN cv.value END) AS artist,
                    MAX(CASE WHEN cv.key = 'file_name' THEN cv.value END) AS file_name,
                    MAX(CASE WHEN cv.key = 'wikidata_qid' THEN cv.value END) AS wikidata_qid,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.winning_provider_id END) AS title_provider_id,
                    MAX(CASE WHEN cv.key = 'title' THEN cv.is_conflicted END) AS title_conflicted,
                    w.curator_state
                FROM works w
                LEFT JOIN editions e2 ON e2.work_id = w.id
                LEFT JOIN media_assets ma2 ON ma2.edition_id = e2.id
                LEFT JOIN canonical_values cv ON cv.entity_id = ma2.id
                GROUP BY w.id, w.media_type, w.wikidata_status, w.curator_state
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
                SELECT DISTINCT rq.entity_id,
                       (SELECT id FROM review_queue rq2
                        WHERE rq2.entity_id = rq.entity_id AND rq2.status = 'Pending'
                        ORDER BY CASE rq2.trigger
                            WHEN 'AuthorityMatchFailed'  THEN 1
                            WHEN 'StagedUnidentifiable'  THEN 2
                            WHEN 'PlaceholderTitle'      THEN 3
                            WHEN 'AmbiguousMediaType'    THEN 4
                            WHEN 'MultipleQidMatches'    THEN 5
                            WHEN 'LowConfidence'         THEN 6
                            WHEN 'MissingQid'            THEN 7
                            WHEN 'ContentMatchFailed'    THEN 8
                            WHEN 'ArtworkUnconfirmed'    THEN 9
                            WHEN 'LanguageMismatch'      THEN 10
                            ELSE 99
                        END LIMIT 1) AS review_id,
                       (SELECT trigger FROM review_queue rq2
                        WHERE rq2.entity_id = rq.entity_id AND rq2.status = 'Pending'
                        ORDER BY CASE rq2.trigger
                            WHEN 'AuthorityMatchFailed'  THEN 1
                            WHEN 'StagedUnidentifiable'  THEN 2
                            WHEN 'PlaceholderTitle'      THEN 3
                            WHEN 'AmbiguousMediaType'    THEN 4
                            WHEN 'MultipleQidMatches'    THEN 5
                            WHEN 'LowConfidence'         THEN 6
                            WHEN 'MissingQid'            THEN 7
                            WHEN 'ContentMatchFailed'    THEN 8
                            WHEN 'ArtworkUnconfirmed'    THEN 9
                            WHEN 'LanguageMismatch'      THEN 10
                            ELSE 99
                        END LIMIT 1) AS trigger,
                       (SELECT MAX(confidence_score) FROM review_queue rq2
                        WHERE rq2.entity_id = rq.entity_id AND rq2.status = 'Pending') AS confidence_score,
                       (SELECT candidates_json FROM review_queue rq2
                        WHERE rq2.entity_id = rq.entity_id AND rq2.status = 'Pending'
                          AND rq2.candidates_json IS NOT NULL
                        LIMIT 1) AS candidates_json
                FROM review_queue rq
                WHERE rq.status = 'Pending'
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
                    wd.hero_url,
                    wd.author,
                    wd.director,
                    wd.artist,
                    wd.file_name,
                    wd.title_provider_id AS match_source,
                    rd.review_id,
                    rd.trigger AS review_trigger,
                    CASE
                        WHEN wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != ''
                             AND wd.wikidata_qid NOT LIKE 'NF%' THEN 1.0
                        WHEN rd.review_id IS NOT NULL THEN 0.5
                        WHEN wd.curator_state = 'provisional' THEN 0.3
                        ELSE 0.0
                    END AS confidence,
                    COALESCE(ul.has_locks, 0) AS has_user_locks,
                    CASE
                        WHEN wd.curator_state = 'rejected' THEN 'Rejected'
                        WHEN wd.curator_state = 'provisional' THEN 'Provisional'
                        WHEN wd.curator_state = 'registered'
                             AND wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != ''
                             AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'Identified'
                        WHEN wd.curator_state = 'registered'
                             AND (wd.wikidata_qid IS NULL OR wd.wikidata_qid = '' OR wd.wikidata_qid LIKE 'NF%')
                             THEN 'Confirmed'
                        WHEN rd.review_id IS NOT NULL THEN 'InReview'
                        WHEN rd.review_id IS NULL
                             AND (wd.wikidata_qid IS NULL OR wd.wikidata_qid = '' OR wd.wikidata_qid LIKE 'NF%')
                             AND wd.cover_url IS NOT NULL AND wd.cover_url != ''
                             THEN 'AwaitingStage2'
                        WHEN wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != ''
                             AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'Identified'
                        ELSE 'Registered'
                    END AS status,
                    CASE WHEN ad.asset_status = 'Conflicted' THEN 1 ELSE 0 END AS has_duplicate,
                    ad.file_path_root,
                    wd.wikidata_status,
                    CASE
                        WHEN wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != '' AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'matched'
                        WHEN rd.review_id IS NOT NULL AND rd.trigger = 'AuthorityMatchFailed' THEN 'failed'
                        WHEN rd.review_id IS NOT NULL THEN 'warning'
                        ELSE 'none'
                    END AS wikidata_match,
                    CASE
                        WHEN wd.cover_url IS NOT NULL AND wd.cover_url != '' THEN 'matched'
                        WHEN rd.review_id IS NOT NULL AND rd.trigger = 'ArtworkUnconfirmed' THEN 'failed'
                        ELSE 'none'
                    END AS retail_match,
                    wd.wikidata_qid,
                    idd.first_claimed_at AS created_at
                FROM work_data wd
                LEFT JOIN asset_data ad ON ad.work_id = wd.entity_id
                LEFT JOIN review_data rd ON rd.entity_id = ad.asset_id
                LEFT JOIN user_lock_data ul ON ul.entity_id = ad.asset_id
                LEFT JOIN ingest_date_data idd ON idd.work_id = wd.entity_id
                WHERE (
                    -- Items are only visible in the Registry when they have a confirmed
                    -- identity (QID), a pending review, or a curator-created provisional entry.
                    -- Unidentified items awaiting hydration stay hidden.
                    (wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != '' AND wd.wikidata_qid NOT LIKE 'NF%')
                    OR rd.review_id IS NOT NULL
                    OR wd.curator_state IS NOT NULL
                )
            )
            """;

        // Build WHERE clause dynamically — must use raw SqliteCommand because
        // Dapper's anonymous-object parameter binding does not support conditional
        // parameter lists, and the WHERE clause itself changes shape at runtime.
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Search))
            conditions.Add(@"(wd.entity_id IN (SELECT si.entity_id FROM search_index si
                WHERE search_index MATCH @ftsQuery) OR fd.file_name LIKE @search)");
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
                conditions.Add("fd.status IN ('Auto', 'Edited')");
            else
                conditions.Add("fd.status = @status");
        }
        if (query.MinConfidence.HasValue)
            conditions.Add("fd.confidence >= @minConfidence");
        if (!string.IsNullOrWhiteSpace(query.MatchSource))
            conditions.Add("fd.match_source = @matchSource");
        if (query.DuplicatesOnly)
            conditions.Add("fd.has_duplicate = 1");
        if (query.MissingUniverseOnly)
            conditions.Add("fd.wikidata_status IN ('missing', 'manual')");
        if (query.MaxDays.HasValue)
            conditions.Add($"fd.created_at >= datetime('now', '-{query.MaxDays.Value} days')");

        var whereClause = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";

        var orderBy = query.Sort switch
        {
            "oldest"       => "ORDER BY fd.created_at ASC, fd.title ASC",
            "title"        => "ORDER BY fd.title ASC, fd.created_at DESC",
            "-title"       => "ORDER BY fd.title DESC, fd.created_at DESC",
            "-confidence"  => "ORDER BY fd.confidence DESC, fd.title ASC",
            "confidence"   => "ORDER BY fd.confidence ASC, fd.title ASC",
            _              => "ORDER BY fd.created_at DESC, fd.title ASC", // "newest" is the default
        };

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
                fd.file_name, fd.author, fd.file_path_root, fd.wikidata_status,
                fd.wikidata_match, fd.retail_match, fd.wikidata_qid, fd.hero_url,
                fd.created_at, fd.director, fd.artist
            FROM full_data fd
            {whereClause}
            {orderBy}
            LIMIT @limit OFFSET @offset
            """;
        AddParameters(dataCmd, query);
        dataCmd.Parameters.AddWithValue("@limit",  query.Limit);
        dataCmd.Parameters.AddWithValue("@offset", query.Offset);

        var items = new List<RegistryItem>();
        using var reader = dataCmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new RegistryItem
            {
                EntityId      = Guid.Parse(reader.GetString(0)),
                Title         = reader.GetString(1),
                Year          = reader.IsDBNull(2)  ? null : reader.GetString(2),
                MediaType     = reader.GetString(3),
                CoverUrl      = reader.IsDBNull(4)  ? null : reader.GetString(4),
                MatchSource   = reader.IsDBNull(5)  ? null : reader.GetString(5),
                Confidence    = reader.GetDouble(6),
                Status        = reader.GetString(7),
                HasDuplicate  = reader.GetInt32(8) == 1,
                ReviewItemId  = reader.IsDBNull(9)  ? null : Guid.Parse(reader.GetString(9)),
                ReviewTrigger = reader.IsDBNull(10) ? null : reader.GetString(10),
                HasUserLocks  = reader.GetInt32(11) == 1,
                FileName      = reader.IsDBNull(12) ? null : reader.GetString(12),
                Author        = reader.IsDBNull(13) ? null : reader.GetString(13),
                WikidataStatus = reader.IsDBNull(15) ? null : reader.GetString(15),
                WikidataMatch  = reader.IsDBNull(16) ? "none" : reader.GetString(16),
                RetailMatch    = reader.IsDBNull(17) ? "none" : reader.GetString(17),
                WikidataQid    = reader.IsDBNull(18) ? null : reader.GetString(18),
                HeroUrl        = reader.IsDBNull(19) ? null : reader.GetString(19),
                CreatedAt      = reader.IsDBNull(20) ? DateTimeOffset.MinValue
                                     : (DateTimeOffset.TryParse(reader.GetString(20), out var createdDt)
                                         ? createdDt
                                         : DateTimeOffset.MinValue),
                Director       = reader.IsDBNull(21) ? null : reader.GetString(21),
                Artist         = reader.IsDBNull(22) ? null : reader.GetString(22),
            });
        }

        // Search filtering is handled by FTS5 MATCH in the SQL query —
        // no post-query re-ranking needed.

        return Task.FromResult(new RegistryPageResult(items, totalCount, query.Offset + items.Count < totalCount));
    }

    public Task<RegistryItemDetail?> GetDetailAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        // Canonical values and claims are stored against media_asset.id, not works.id.
        // Resolve the asset ID through the editions → media_assets chain.
        var assetIdStr = conn.ExecuteScalar<string?>("""
            SELECT MIN(ma.id) AS id
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id = @entityId
            """, new { entityId = entityId.ToString() });

        if (assetIdStr is null)
            return Task.FromResult<RegistryItemDetail?>(null);

        // Load all canonical values for this entity (keyed by asset ID)
        var canonicalValues = conn.Query<(string Key, string Value, int IsConflicted,
            string? WinningProviderId, int NeedsReview, string LastScoredAt)>("""
            SELECT key AS Key, value AS Value, is_conflicted AS IsConflicted,
                   winning_provider_id AS WinningProviderId, needs_review AS NeedsReview,
                   last_scored_at AS LastScoredAt
            FROM canonical_values
            WHERE entity_id = @assetId
            ORDER BY key
            """, new { assetId = assetIdStr })
            .Select(r => new RegistryCanonicalValue(
                Key:               r.Key,
                Value:             r.Value,
                IsConflicted:      r.IsConflicted == 1,
                WinningProviderId: r.WinningProviderId,
                NeedsReview:       r.NeedsReview == 1,
                LastScoredAt:      DateTimeOffset.TryParse(r.LastScoredAt, out var dt) ? dt : DateTimeOffset.MinValue))
            .ToList();

        if (canonicalValues.Count == 0)
            return Task.FromResult<RegistryItemDetail?>(null);

        // Load claim history (also keyed by asset ID)
        var claims = conn.Query<(string Id, string ClaimKey, string ClaimValue, string ProviderId,
            double Confidence, int IsUserLocked, string ClaimedAt)>("""
            SELECT id AS Id, claim_key AS ClaimKey, claim_value AS ClaimValue,
                   provider_id AS ProviderId, confidence AS Confidence,
                   is_user_locked AS IsUserLocked, claimed_at AS ClaimedAt
            FROM metadata_claims
            WHERE entity_id = @assetId
            ORDER BY claimed_at DESC
            """, new { assetId = assetIdStr })
            .Select(r => new RegistryClaimRecord(
                Id:           Guid.Parse(r.Id),
                ClaimKey:     r.ClaimKey,
                ClaimValue:   r.ClaimValue,
                ProviderId:   Guid.Parse(r.ProviderId),
                Confidence:   r.Confidence,
                IsUserLocked: r.IsUserLocked == 1,
                ClaimedAt:    DateTimeOffset.TryParse(r.ClaimedAt, out var cdt) ? cdt : DateTimeOffset.MinValue))
            .ToList();

        // Load review queue entry if any (review_queue uses asset ID as entity_id)
        var rqRow = conn.QueryFirstOrDefault<(string Id, string Trigger, double? ConfidenceScore,
            string? Detail, string? CandidatesJson)>("""
            SELECT id AS Id, trigger AS Trigger, confidence_score AS ConfidenceScore,
                   detail AS Detail, candidates_json AS CandidatesJson
            FROM review_queue
            WHERE (entity_id = @assetId OR entity_id = @entityId) AND status = 'Pending'
            ORDER BY CASE trigger
                WHEN 'AuthorityMatchFailed'  THEN 1
                WHEN 'StagedUnidentifiable'  THEN 2
                WHEN 'PlaceholderTitle'      THEN 3
                WHEN 'AmbiguousMediaType'    THEN 4
                WHEN 'MultipleQidMatches'    THEN 5
                WHEN 'LowConfidence'         THEN 6
                WHEN 'MissingQid'            THEN 7
                WHEN 'ContentMatchFailed'    THEN 8
                WHEN 'ArtworkUnconfirmed'    THEN 9
                WHEN 'LanguageMismatch'      THEN 10
                ELSE 99
            END
            LIMIT 1
            """, new { assetId = assetIdStr, entityId = entityId.ToString() });

        Guid? reviewItemId    = rqRow == default ? null : Guid.Parse(rqRow.Id);
        string? reviewTrigger = rqRow == default ? null : rqRow.Trigger;
        string? reviewDetail  = rqRow == default ? null : rqRow.Detail;
        string? candidatesJson = rqRow == default ? null : rqRow.CandidatesJson;
        double? reviewConfidence = rqRow == default ? null : rqRow.ConfidenceScore;

        // Load media asset info
        var maRow = conn.QueryFirstOrDefault<(string? FilePath, string? ContentHash)>("""
            SELECT ma.file_path_root AS FilePath, ma.content_hash AS ContentHash
            FROM editions e
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE e.work_id = @entityId
            LIMIT 1
            """, new { entityId = entityId.ToString() });

        string? filePath    = maRow == default ? null : maRow.FilePath;
        string? contentHash = maRow == default ? null : maRow.ContentHash;
        string? fileName    = filePath is not null ? Path.GetFileName(filePath) : null;

        // Load wikidata_status, media_type, and match_level from the work
        var workRow = conn.QueryFirstOrDefault<(string? WikidataStatus, string? MediaType, string? MatchLevel, string? CuratorState)>("""
            SELECT wikidata_status AS WikidataStatus, media_type AS MediaType,
                   match_level AS MatchLevel, curator_state AS CuratorState
            FROM works WHERE id = @entityId
            """, new { entityId = entityId.ToString() });

        string? wikidataStatus = workRow == default ? null : workRow.WikidataStatus;
        string  mediaType      = workRow == default ? "" : workRow.MediaType ?? "";
        string  matchLevel     = workRow == default ? "work" : workRow.MatchLevel ?? "work";
        string? curatorState   = workRow == default ? null : workRow.CuratorState;

        // Helper to get canonical value by key (with year-key fallback aliases)
        string? cv(string key)
        {
            var val = canonicalValues.FirstOrDefault(v => v.Key == key)?.Value;
            if (val is null && key == "release_year")
                val = canonicalValues.FirstOrDefault(v => v.Key == "date" || v.Key == "year")?.Value;
            return val;
        }
        var hasUserLocks = claims.Any(c => c.IsUserLocked);
        var titleProvider = canonicalValues.FirstOrDefault(v => v.Key == "title")?.WinningProviderId;
        var wikidataQid = cv("wikidata_qid");

        // Status logic mirrors GetPageAsync: curator_state overrides, then review, then QID check
        bool hasValidQid = !string.IsNullOrEmpty(wikidataQid)
                        && !wikidataQid.StartsWith("NF", StringComparison.OrdinalIgnoreCase);
        var status = curatorState switch
        {
            "rejected"    => "Rejected",
            "provisional" => "Provisional",
            "registered"  => hasValidQid ? "Identified" : "Confirmed",
            _ => reviewItemId.HasValue ? "InReview"
               : (!string.IsNullOrEmpty(cv("cover_url")) && !hasValidQid)
                   ? "AwaitingStage2"
               : hasValidQid
                   ? "Identified"
               : hasUserLocks ? "Edited"
               : "Auto",
        };

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
        if (!string.IsNullOrEmpty(wikidataQid) && !bridgeIds.ContainsKey("wikidata_qid"))
            bridgeIds["wikidata_qid"] = wikidataQid;

        // Derive match confidence: review score → QID exists (0.95) → no match (0%)
        var matchConfidence = reviewConfidence
            ?? (!string.IsNullOrEmpty(wikidataQid) ? 0.95 : 0.0);

        var detail = new RegistryItemDetail
        {
            EntityId       = entityId,
            Title          = cv("title") ?? "Untitled",
            Year           = cv("release_year"),
            MediaType      = mediaType,
            CoverUrl       = cv("cover_url"),
            HeroUrl        = cv("hero"),
            Confidence     = matchConfidence,
            Status         = status,
            MatchSource    = titleProvider,
            Author         = cv("author"),
            Director       = cv("director"),
            Cast           = cv("cast"),
            Language       = cv("language"),
            Genre          = cv("genre"),
            Runtime        = cv("runtime"),
            Description    = cv("description"),
            Series         = cv("series"),
            SeriesPosition = cv("series_position"),
            Narrator       = cv("narrator"),
            Rating         = cv("rating"),
            WikidataQid    = wikidataQid,
            WikidataStatus = wikidataStatus,
            FileName       = fileName ?? cv("file_name"),
            FilePath       = filePath,
            ContentHash    = contentHash,
            ReviewItemId   = reviewItemId,
            ReviewTrigger  = reviewTrigger,
            ReviewDetail   = reviewDetail,
            CandidatesJson = candidatesJson,
            HasUserLocks   = hasUserLocks,
            MatchLevel     = matchLevel,
            CanonicalValues = canonicalValues,
            ClaimHistory   = claims,
            BridgeIds      = bridgeIds,
        };

        return Task.FromResult<RegistryItemDetail?>(detail);
    }

    public Task<RegistryStatusCounts> GetStatusCountsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM works) AS Total,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM review_queue rq
                 INNER JOIN media_assets ma ON ma.id = rq.entity_id
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE rq.status = 'Pending') AS NeedsReview,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM metadata_claims mc
                 INNER JOIN media_assets ma ON ma.id = mc.entity_id
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE mc.is_user_locked = 1) AS Edited,
                (SELECT COUNT(*) FROM media_assets WHERE status = 'Conflicted') AS Duplicate,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM media_assets ma
                 INNER JOIN editions e ON e.id = ma.edition_id
                 WHERE (ma.file_path_root LIKE '%/.staging/%'
                    OR ma.file_path_root LIKE '%\.staging\%')
                   AND NOT EXISTS (
                     SELECT 1 FROM review_queue rq
                     WHERE rq.entity_id = ma.id AND rq.status = 'Pending'
                   )) AS Staging,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM editions e
                 JOIN media_assets ma ON ma.edition_id = e.id
                 LEFT JOIN canonical_values cv_cover ON cv_cover.entity_id = ma.id AND cv_cover.key = 'cover_url'
                 WHERE (cv_cover.value IS NULL OR TRIM(cv_cover.value) = '')
                   AND EXISTS (
                     SELECT 1 FROM canonical_values cv2
                     WHERE cv2.entity_id = ma.id AND cv2.key = 'wikidata_qid'
                     AND cv2.value IS NOT NULL AND TRIM(cv2.value) != ''
                   )
                ) AS MissingImages,
                -- "RecentlyUpdated" counts items where at least one canonical value was
                -- re-scored in the last 24 hours AND the item has an earlier metadata claim
                -- (i.e. it was already in the system before today's scoring pass).
                -- This excludes newly-ingested items whose first-ever canonical values
                -- happen to fall in the same 24-hour window.
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM editions e
                 JOIN media_assets ma ON ma.edition_id = e.id
                 JOIN canonical_values cv ON cv.entity_id = ma.id
                 WHERE cv.last_scored_at >= datetime('now', '-24 hours')
                   AND EXISTS (
                     SELECT 1 FROM metadata_claims mc2
                     WHERE mc2.entity_id = ma.id
                       AND mc2.claimed_at < datetime('now', '-24 hours')
                   )) AS RecentlyUpdated,
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM editions e
                 JOIN media_assets ma ON ma.edition_id = e.id
                 JOIN canonical_values cv ON cv.entity_id = ma.id AND cv.key = 'overall_confidence'
                 WHERE CAST(cv.value AS REAL) BETWEEN 0.40 AND 0.85
                   AND NOT EXISTS (SELECT 1 FROM review_queue rq WHERE rq.entity_id = ma.id AND rq.status = 'Pending')
                ) AS LowConfidence,
                (SELECT COUNT(*) FROM works WHERE curator_state = 'rejected') AS Rejected
            """;

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var total           = reader.GetInt32(0);
            var needsReview     = reader.GetInt32(1);
            var edited          = reader.GetInt32(2);
            var duplicate       = reader.GetInt32(3);
            var staging         = reader.GetInt32(4);
            var missingImages   = reader.GetInt32(5);
            var recentlyUpdated = reader.GetInt32(6);
            var lowConfidence   = reader.GetInt32(7);
            var rejected        = reader.GetInt32(8);
            var auto = total - needsReview - edited - duplicate - staging - rejected;
            return Task.FromResult(new RegistryStatusCounts(
                total, needsReview, Math.Max(auto, 0), edited, duplicate, staging,
                missingImages, recentlyUpdated, lowConfidence, rejected));
        }

        return Task.FromResult(new RegistryStatusCounts(0, 0, 0, 0, 0, 0));
    }

    public Task<RegistryFourStateCounts> GetFourStateCountsAsync(Guid? batchId = null, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        // ── Batch-scoped: read directly from ingestion_batches counters ──
        if (batchId.HasValue)
        {
            var batch = conn.QueryFirstOrDefault<(int FilesIdentified, int FilesReview,
                int FilesNoMatch, int FilesFailed)>("""
                SELECT files_registered AS FilesIdentified,
                       files_review     AS FilesReview,
                       files_no_match   AS FilesNoMatch,
                       files_failed     AS FilesFailed
                FROM ingestion_batches
                WHERE id = @batchId
                """, new { batchId = batchId.Value.ToString() });

            // Trigger breakdown for batch-scoped: join through ingestion_log → media_assets → review_queue
            var batchTriggers = conn.Query<(string Trigger, int Count)>("""
                SELECT rq.trigger AS Trigger, COUNT(*) AS Count
                FROM review_queue rq
                INNER JOIN media_assets ma ON ma.id = rq.entity_id
                INNER JOIN ingestion_log il ON il.content_hash = ma.content_hash
                WHERE rq.status = 'Pending'
                  AND il.ingestion_run_id = @batchId
                GROUP BY rq.trigger
                ORDER BY Count DESC
                """, new { batchId = batchId.Value.ToString() })
                .ToDictionary(r => r.Trigger, r => r.Count);

            // Map legacy batch counters to new 4-state model:
            // NoMatch + Failed → counted as InReview (items needing attention)
            // Provisional and Rejected are 0 for batch-scoped counts (batches don't track these)
            return Task.FromResult(new RegistryFourStateCounts(
                batch.FilesIdentified,
                batch.FilesReview + batch.FilesNoMatch + batch.FilesFailed,
                0, // Provisional — not tracked per-batch
                0, // Rejected — not tracked per-batch
                0, // PersonCount — not tracked per-batch
                0, // HubCount — not tracked per-batch
                batchTriggers));
        }

        // ── Cross-batch: compute from works + review_queue + ingestion_batches ──

        // Pending review asset IDs (for exclusion from other states)
        // NeedsReview = works with at least one pending review_queue entry
        // Registered = works with valid QID (not NF%) and NOT in review
        // NoMatch = works without valid QID and NOT in review
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT
                -- Registered: has valid QID, no curator_state override, no pending review
                (SELECT COUNT(*) FROM works w
                 WHERE (w.curator_state IS NULL OR w.curator_state = '')
                 AND EXISTS (
                     SELECT 1 FROM editions e3
                     INNER JOIN media_assets ma3 ON ma3.edition_id = e3.id
                     INNER JOIN canonical_values cv3 ON cv3.entity_id = ma3.id
                     WHERE e3.work_id = w.id
                       AND cv3.key = 'wikidata_qid'
                       AND cv3.value IS NOT NULL AND cv3.value != ''
                       AND cv3.value NOT LIKE 'NF%'
                 )
                 AND NOT EXISTS (
                     SELECT 1 FROM editions e2
                     INNER JOIN media_assets ma2 ON ma2.edition_id = e2.id
                     INNER JOIN review_queue rq ON rq.entity_id = ma2.id
                     WHERE e2.work_id = w.id AND rq.status = 'Pending'
                 )) AS Identified,

                -- InReview: has pending review_queue entry AND not provisional/rejected
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM review_queue rq
                 INNER JOIN media_assets ma ON ma.id = rq.entity_id
                 INNER JOIN editions e ON e.id = ma.edition_id
                 INNER JOIN works w ON w.id = e.work_id
                 WHERE rq.status = 'Pending'
                   AND (w.curator_state IS NULL OR w.curator_state = '')) AS InReview,

                -- Provisional: curator_state = 'provisional'
                (SELECT COUNT(*) FROM works WHERE curator_state = 'provisional') AS Provisional,

                -- Rejected: curator_state = 'rejected'
                (SELECT COUNT(*) FROM works WHERE curator_state = 'rejected') AS Rejected,

                (SELECT COUNT(*) FROM persons) AS PersonCount,
                (SELECT COUNT(DISTINCT id) FROM hubs) AS HubCount
            """;

        int identified = 0, inReview = 0, provisional = 0, rejected = 0, personCount = 0, hubCount = 0;
        using (var reader = cmd.ExecuteReader())
        {
            if (reader.Read())
            {
                identified  = reader.GetInt32(0);
                inReview    = reader.GetInt32(1);
                provisional = reader.GetInt32(2);
                rejected    = reader.GetInt32(3);
                personCount = reader.GetInt32(4);
                hubCount    = reader.GetInt32(5);
            }
        }

        // Trigger counts: per-trigger breakdown within InReview
        // Exclude provisional/rejected works to keep counts consistent with the new model
        var triggerCounts = conn.Query<(string Trigger, int Count)>("""
            SELECT rq.trigger AS Trigger, COUNT(DISTINCT e.work_id) AS Count
            FROM review_queue rq
            INNER JOIN media_assets ma ON ma.id = rq.entity_id
            INNER JOIN editions e ON e.id = ma.edition_id
            INNER JOIN works w ON w.id = e.work_id
            WHERE rq.status = 'Pending'
              AND (w.curator_state IS NULL OR w.curator_state = '')
            GROUP BY rq.trigger
            ORDER BY Count DESC
            """)
            .ToDictionary(r => r.Trigger, r => r.Count);

        return Task.FromResult(new RegistryFourStateCounts(
            identified, inReview, provisional, rejected, personCount, hubCount, triggerCounts));
    }

    public Task<Dictionary<string, int>> GetMediaTypeCountsAsync(CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();
        var rows = conn.Query<(string MediaType, int Count)>("""
            SELECT cv.value AS MediaType, COUNT(DISTINCT w.id) AS Count
            FROM canonical_values cv
            JOIN media_assets ma ON ma.id = cv.entity_id
            JOIN editions e ON e.id = ma.edition_id
            JOIN works w ON w.id = e.work_id
            WHERE cv.key = 'media_type'
            GROUP BY cv.value
            """);
        return Task.FromResult(rows.ToDictionary(r => r.MediaType, r => r.Count));
    }

    // ── Private helpers ──────────────────────────────────────────────────────

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
                cmd.Parameters.AddWithValue("@mediaType", NormalizeMediaType(types[0]));
            else
            {
                for (int i = 0; i < types.Length; i++)
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

    /// <summary>
    /// Maps legacy or variant media type strings to the canonical <see cref="Domain.Enums.MediaType"/>
    /// enum name stored in the database. Prevents filter mismatches when UI sends a different casing
    /// or legacy name (e.g. "Epub" instead of "Books", "Audiobook" instead of "Audiobooks").
    /// </summary>
    private static string NormalizeMediaType(string raw) => raw.ToUpperInvariant() switch
    {
        "EPUB" or "BOOK" or "EBOOK"         => "Books",
        "AUDIOBOOK"                          => "Audiobooks",
        "MOVIE"                              => "Movies",
        "COMIC"                              => "Comics",
        "PODCAST"                            => "Podcasts",
        _ => raw, // Already matches enum name (Books, Audiobooks, Movies, TV, Comics, Podcasts, Music)
    };
}
