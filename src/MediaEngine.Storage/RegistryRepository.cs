using System.Linq;
using Dapper;
using MediaEngine.Domain;
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
        var wikidataId       = WellKnownProviders.Wikidata.ToString();
        var localProcessorId = WellKnownProviders.LocalProcessor.ToString();
        var libraryScanId    = WellKnownProviders.LibraryScanner.ToString();
        var stagingFilter = query.IncludeAll ? "" : """
                    WHERE ma.file_path_root NOT LIKE '%/.data/staging/%'
                      AND ma.file_path_root NOT LIKE '%\.data\staging\%'
                      AND ma.file_path_root NOT LIKE '%/.data\staging/%'
            """;
        var visibilityFilter = query.IncludeAll ? "" : """
                    WHERE (
                        (wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != '' AND wd.wikidata_qid NOT LIKE 'NF%')
                        OR rd.review_id IS NOT NULL
                        OR wd.curator_state IN ('provisional', 'rejected')
                    )
            """;
        // Phase 4 — lineage-aware reads. Two pivot CTEs:
        //   • acv (asset_cv) — canonical_values keyed on media_assets.id, holding
        //     SELF-scope fields (title, season_number, episode_number, etc.)
        //   • wcv (work_cv)  — canonical_values keyed on the TOPMOST Work id
        //     (the SHOW for TV, the ALBUM for music, the SERIES for comics, the
        //     movie's own Work for standalone movies), holding PARENT-scope
        //     fields (year, cover_url, genre, description, show_name, album,
        //     director-for-movies, etc.).
        //
        // The router (ScoringHelper.PersistAndScoreWithLineageAsync) writes each
        // claim to exactly ONE target based on ClaimScopeRegistry, so each field
        // appears in exactly one CTE — no fallback, no COALESCE between them.
        // Fields whose scope varies by media type (director, runtime) use a
        // single deterministic source per row via CASE on w.media_type.
        var sql = $"""
            WITH asset_cv AS (
                SELECT e.work_id AS work_id,
                       cv.key, cv.value, cv.winning_provider_id, cv.is_conflicted
                FROM editions e
                JOIN media_assets ma ON ma.edition_id = e.id
                JOIN canonical_values cv ON cv.entity_id = ma.id
            ),
            work_cv AS (
                -- canonical_values stored against the topmost Work id.
                -- Walks the parent_work_id chain up to two levels (TV: episode →
                -- season → show; music: track → album; movies: standalone).
                SELECT w.id AS work_id, cv.key, cv.value
                FROM works w
                LEFT JOIN works p  ON p.id  = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                JOIN canonical_values cv
                  ON cv.entity_id = COALESCE(gp.id, p.id, w.id)
            ),
            work_data AS (
                SELECT
                    w.id AS entity_id,
                    w.media_type,
                    w.wikidata_status,
                    -- ── Self-scope (asset row) ──
                    MAX(CASE WHEN acv.key = 'title'           THEN acv.value END) AS title,
                    MAX(CASE WHEN acv.key = 'series_position' THEN acv.value END) AS series_position,
                    MAX(CASE WHEN acv.key = 'rating'          THEN acv.value END) AS rating,
                    MAX(CASE WHEN acv.key = 'track_number'    THEN acv.value END) AS track_number,
                    MAX(CASE WHEN acv.key = 'season_number'   THEN acv.value END) AS season_number,
                    MAX(CASE WHEN acv.key = 'episode_number'  THEN acv.value END) AS episode_number,
                    MAX(CASE WHEN acv.key = 'episode_title'   THEN acv.value END) AS episode_title,
                    MAX(CASE WHEN acv.key = 'duration'        THEN acv.value END) AS duration,
                    MAX(CASE WHEN acv.key = 'hero'            THEN acv.value END) AS hero_url,
                    MAX(CASE WHEN acv.key = 'file_name'       THEN acv.value END) AS file_name,
                    MAX(CASE WHEN acv.key = 'title' THEN acv.winning_provider_id END) AS title_provider_id,
                    MAX(CASE WHEN acv.key = 'title' THEN acv.is_conflicted END)       AS title_conflicted,
                    -- ── Parent-scope (root parent Work row) ──
                    MAX(CASE WHEN wcv.key IN ('release_year','date','year') THEN wcv.value END) AS year,
                    MAX(CASE WHEN wcv.key = 'cover_url'    THEN wcv.value END) AS cover_url,
                    -- Multi-author display: prefer canonical_value_arrays
                    -- (keyed on the topmost Work id since author is Parent-scoped),
                    -- format as "A & B" or "A & B + N more", and fall back to the
                    -- pivoted single-string canonical author when no array exists.
                    COALESCE(
                        (SELECT
                            CASE
                                WHEN cnt.total = 1 THEN a1.value
                                WHEN cnt.total = 2 THEN a1.value || ' & ' || a2.value
                                ELSE a1.value || ' & ' || a2.value || ' + ' || (cnt.total - 2) || ' more'
                            END
                         FROM (SELECT COUNT(DISTINCT cva0.value) AS total
                               FROM canonical_value_arrays cva0
                               LEFT JOIN works pw  ON pw.id  = w.parent_work_id
                               LEFT JOIN works gpw ON gpw.id = pw.parent_work_id
                               WHERE cva0.entity_id = COALESCE(gpw.id, pw.id, w.id)
                                 AND cva0.key = 'author') cnt
                         LEFT JOIN (SELECT DISTINCT cva1.value
                                    FROM canonical_value_arrays cva1
                                    LEFT JOIN works pw1  ON pw1.id  = w.parent_work_id
                                    LEFT JOIN works gpw1 ON gpw1.id = pw1.parent_work_id
                                    WHERE cva1.entity_id = COALESCE(gpw1.id, pw1.id, w.id)
                                      AND cva1.key = 'author'
                                    ORDER BY cva1.ordinal LIMIT 1) a1 ON 1=1
                         LEFT JOIN (SELECT DISTINCT cva2.value
                                    FROM canonical_value_arrays cva2
                                    LEFT JOIN works pw2  ON pw2.id  = w.parent_work_id
                                    LEFT JOIN works gpw2 ON gpw2.id = pw2.parent_work_id
                                    WHERE cva2.entity_id = COALESCE(gpw2.id, pw2.id, w.id)
                                      AND cva2.key = 'author'
                                    ORDER BY cva2.ordinal LIMIT 1 OFFSET 1) a2 ON cnt.total >= 2
                         WHERE cnt.total >= 1),
                        MAX(CASE WHEN wcv.key = 'author' THEN wcv.value END)
                    ) AS author,
                    MAX(CASE WHEN wcv.key = 'artist'       THEN wcv.value END) AS artist,
                    MAX(CASE WHEN wcv.key = 'series'       THEN wcv.value END) AS series,
                    MAX(CASE WHEN wcv.key = 'narrator'     THEN wcv.value END) AS narrator,
                    MAX(CASE WHEN wcv.key = 'genre'        THEN wcv.value END) AS genre,
                    MAX(CASE WHEN wcv.key = 'album'        THEN wcv.value END) AS album,
                    MAX(CASE WHEN wcv.key = 'show_name'    THEN wcv.value END) AS show_name,
                    MAX(CASE WHEN wcv.key = 'network'      THEN wcv.value END) AS network,
                    -- wikidata_qid is Self-scoped (default): for TV the QID is the
                    -- episode's QID, for music it's the track's QID, for movies it
                    -- collapses onto the movie's own Work but is still routed to the
                    -- asset row by ScoringHelper. Read from the asset CTE only.
                    MAX(CASE WHEN acv.key = 'wikidata_qid' THEN acv.value END) AS wikidata_qid,
                    -- ── Mixed fields (scope depends on media_type) ──
                    -- director: Parent for Movies (works.id), Self for TV (asset).
                    -- runtime:  Parent for Movies, Self elsewhere.
                    CASE WHEN w.media_type = 'Movies'
                         THEN MAX(CASE WHEN wcv.key = 'director' THEN wcv.value END)
                         ELSE MAX(CASE WHEN acv.key = 'director' THEN acv.value END)
                    END AS director,
                    CASE WHEN w.media_type = 'Movies'
                         THEN MAX(CASE WHEN wcv.key = 'runtime' THEN wcv.value END)
                         ELSE MAX(CASE WHEN acv.key = 'runtime' THEN acv.value END)
                    END AS runtime,
                    (SELECT pr.name || ': ' || mc_rt.claim_value
                     FROM metadata_claims mc_rt
                     INNER JOIN media_assets ma_rt ON ma_rt.id = mc_rt.entity_id
                     INNER JOIN editions e_rt ON e_rt.id = ma_rt.edition_id
                     INNER JOIN provider_registry pr ON pr.id = mc_rt.provider_id
                     WHERE e_rt.work_id = w.id
                       AND mc_rt.claim_key = 'title'
                       AND mc_rt.provider_id != '{wikidataId}'
                       AND mc_rt.provider_id != 'local_filesystem'
                     ORDER BY mc_rt.confidence DESC
                     LIMIT 1
                    ) AS retail_match_detail,
                    w.curator_state
                FROM works w
                LEFT JOIN asset_cv acv ON acv.work_id = w.id
                LEFT JOIN work_cv  wcv ON wcv.work_id = w.id
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
                {stagingFilter}
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
                SELECT DISTINCT e_rv.work_id AS entity_id,
                       (SELECT rq2.id FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id AND rq2.status = 'Pending'
                        ORDER BY CASE rq2.trigger
                            WHEN 'AuthorityMatchFailed'  THEN 1
                            WHEN 'RetailMatchFailed'     THEN 1
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
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id AND rq2.status = 'Pending'
                        ORDER BY CASE rq2.trigger
                            WHEN 'AuthorityMatchFailed'  THEN 1
                            WHEN 'RetailMatchFailed'     THEN 1
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
                       (SELECT MAX(rq2.confidence_score) FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id AND rq2.status = 'Pending') AS confidence_score,
                       (SELECT rq2.candidates_json FROM review_queue rq2
                        INNER JOIN media_assets ma_rv2 ON ma_rv2.id = rq2.entity_id
                        INNER JOIN editions e_rv2 ON e_rv2.id = ma_rv2.edition_id
                        WHERE e_rv2.work_id = e_rv.work_id AND rq2.status = 'Pending'
                          AND rq2.candidates_json IS NOT NULL
                        LIMIT 1) AS candidates_json
                FROM review_queue rq
                INNER JOIN media_assets ma_rv ON ma_rv.id = rq.entity_id
                INNER JOIN editions e_rv ON e_rv.id = ma_rv.edition_id
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
                    wd.duration,
                    wd.retail_match_detail,
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
                        WHEN rd.review_id IS NOT NULL THEN 'InReview'
                        WHEN wd.curator_state = 'registered'
                             AND wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != ''
                             AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'Identified'
                        WHEN wd.curator_state = 'registered'
                             AND (wd.wikidata_qid IS NULL OR wd.wikidata_qid = '' OR wd.wikidata_qid LIKE 'NF%')
                             THEN 'Confirmed'
                        WHEN rd.review_id IS NULL
                             AND (wd.wikidata_qid IS NULL OR wd.wikidata_qid = '' OR wd.wikidata_qid LIKE 'NF%')
                             AND wd.cover_url IS NOT NULL AND wd.cover_url != ''
                             THEN 'AwaitingStage2'
                        WHEN wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != ''
                             AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'Identified'
                        ELSE 'Registered'
                    END AS status,
                    CASE WHEN ad.asset_status = 'Conflicted' THEN 1 ELSE 0 END AS has_duplicate,
                    ad.file_path_root AS file_path,
                    wd.wikidata_status,
                    CASE
                        WHEN wd.wikidata_qid IS NOT NULL AND wd.wikidata_qid != '' AND wd.wikidata_qid NOT LIKE 'NF%' THEN 'matched'
                        WHEN rd.review_id IS NOT NULL AND rd.trigger = 'AuthorityMatchFailed' THEN 'failed'
                        WHEN rd.review_id IS NOT NULL AND rd.trigger = 'RetailMatchFailed' THEN 'failed'
                        WHEN rd.review_id IS NOT NULL THEN 'warning'
                        ELSE 'none'
                    END AS wikidata_match,
                    CASE
                        WHEN EXISTS (
                            SELECT 1 FROM metadata_claims mc_retail
                            INNER JOIN media_assets ma_r ON ma_r.id = mc_retail.entity_id
                            INNER JOIN editions e_r ON e_r.id = ma_r.edition_id
                            WHERE e_r.work_id = wd.entity_id
                              AND mc_retail.provider_id != '{wikidataId}'
                              AND mc_retail.provider_id != 'local_filesystem'
                              AND mc_retail.provider_id != '{localProcessorId}'
                              AND mc_retail.provider_id != '{libraryScanId}'
                        ) THEN 'matched'
                        WHEN rd.review_id IS NOT NULL AND rd.trigger IN ('RetailMatchFailed', 'ArtworkUnconfirmed') THEN 'failed'
                        ELSE 'none'
                    END AS retail_match,
                    wd.wikidata_qid,
                    idd.first_claimed_at AS created_at
                FROM work_data wd
                LEFT JOIN asset_data ad ON ad.work_id = wd.entity_id
                LEFT JOIN review_data rd ON rd.entity_id = wd.entity_id
                LEFT JOIN user_lock_data ul ON ul.entity_id = ad.asset_id
                LEFT JOIN ingest_date_data idd ON idd.work_id = wd.entity_id
                {visibilityFilter}
            )
            """;

        // Build WHERE clause dynamically — must use raw SqliteCommand because
        // Dapper's anonymous-object parameter binding does not support conditional
        // parameter lists, and the WHERE clause itself changes shape at runtime.
        var conditions = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.Search))
            conditions.Add(@"(fd.entity_id IN (SELECT si.entity_id FROM search_index si
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
        else if (!query.IncludeAll)
        {
            // Default: exclude items that belong in the Action Centre.
            // InReview and Rejected items are only shown when explicitly
            // requested via the Action Centre's status filters.
            conditions.Add("fd.status NOT IN ('InReview', 'Rejected')");
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
                fd.file_name, fd.author, fd.file_path, fd.wikidata_status,
                fd.wikidata_match, fd.retail_match, fd.wikidata_qid, fd.hero_url,
                fd.created_at, fd.director, fd.artist, fd.retail_match_detail,
                fd.series, fd.series_position, fd.narrator, fd.genre,
                fd.runtime, fd.rating, fd.album, fd.track_number,
                fd.season_number, fd.episode_number,
                fd.show_name, fd.duration, fd.episode_title, fd.network
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
                FilePath      = reader.IsDBNull(14) ? null : reader.GetString(14),
                WikidataStatus = reader.IsDBNull(15) ? null : reader.GetString(15),
                WikidataMatch  = reader.IsDBNull(16) ? "none" : reader.GetString(16),
                RetailMatch    = reader.IsDBNull(17) ? "none" : reader.GetString(17),
                WikidataQid    = reader.IsDBNull(18) ? null : reader.GetString(18),
                HeroUrl        = reader.IsDBNull(19) ? null : reader.GetString(19),
                CreatedAt      = reader.IsDBNull(20) ? DateTimeOffset.MinValue
                                     : (DateTimeOffset.TryParse(reader.GetString(20), out var createdDt)
                                         ? createdDt
                                         : DateTimeOffset.MinValue),
                Director          = reader.IsDBNull(21) ? null : reader.GetString(21),
                Artist            = reader.IsDBNull(22) ? null : reader.GetString(22),
                RetailMatchDetail = reader.IsDBNull(23) ? null : reader.GetString(23),
                Series            = reader.IsDBNull(24) ? null : reader.GetString(24),
                SeriesPosition    = reader.IsDBNull(25) ? null : reader.GetString(25),
                Narrator          = reader.IsDBNull(26) ? null : reader.GetString(26),
                Genre             = reader.IsDBNull(27) ? null : reader.GetString(27),
                Runtime           = reader.IsDBNull(28) ? null : reader.GetString(28),
                Rating            = reader.IsDBNull(29) ? null : reader.GetString(29),
                Album             = reader.IsDBNull(30) ? null : reader.GetString(30),
                TrackNumber       = reader.IsDBNull(31) ? null : reader.GetString(31),
                SeasonNumber      = reader.IsDBNull(32) ? null : reader.GetString(32),
                EpisodeNumber     = reader.IsDBNull(33) ? null : reader.GetString(33),
                ShowName          = reader.IsDBNull(34) ? null : reader.GetString(34),
                Duration          = reader.IsDBNull(35) ? null : reader.GetString(35),
                EpisodeTitle      = reader.IsDBNull(36) ? null : reader.GetString(36),
                Network           = reader.IsDBNull(37) ? null : reader.GetString(37),
            });
        }

        // Search filtering is handled by FTS5 MATCH in the SQL query —
        // no post-query re-ranking needed.

        return Task.FromResult(new RegistryPageResult(items, totalCount, query.Offset + items.Count < totalCount));
    }

    public Task<RegistryItemDetail?> GetDetailAsync(Guid entityId, CancellationToken ct = default)
    {
        using var conn = _db.CreateConnection();

        // Phase 4 — lineage-aware detail load. We need TWO canonical_values rowsets:
        //   • assetId          → Self-scope fields (title, episode_number, etc.)
        //   • rootParentWorkId → Parent-scope fields (year, cover_url, genre, etc.)
        //
        // Walk the parent_work_id chain up to two levels via WorkRepository's
        // canonical lineage SQL, returning the topmost Work id.
        var lineageRow = conn.QueryFirstOrDefault<(string AssetId, string RootParentWorkId, string MediaType)>("""
            SELECT MIN(ma.id)                                      AS AssetId,
                   COALESCE(gp.id, p.id, w.id)                     AS RootParentWorkId,
                   w.media_type                                    AS MediaType
            FROM works w
            LEFT JOIN works p  ON p.id  = w.parent_work_id
            LEFT JOIN works gp ON gp.id = p.parent_work_id
            LEFT JOIN editions e         ON e.work_id     = w.id
            LEFT JOIN media_assets ma    ON ma.edition_id = e.id
            WHERE w.id = @entityId
            GROUP BY w.id
            """, new { entityId = entityId.ToString() });

        if (lineageRow == default || lineageRow.AssetId is null)
            return Task.FromResult<RegistryItemDetail?>(null);

        var assetIdStr     = lineageRow.AssetId;
        var rootParentStr  = lineageRow.RootParentWorkId;

        // Load self-scope canonical values from the asset row.
        var selfValues = conn.Query<(string Key, string Value, int IsConflicted,
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

        // Load parent-scope canonical values from the topmost Work row.
        // For standalone media (movies, single-volume books) the topmost Work id
        // equals the work itself, so this still finds the correct row.
        var parentValues = conn.Query<(string Key, string Value, int IsConflicted,
            string? WinningProviderId, int NeedsReview, string LastScoredAt)>("""
            SELECT key AS Key, value AS Value, is_conflicted AS IsConflicted,
                   winning_provider_id AS WinningProviderId, needs_review AS NeedsReview,
                   last_scored_at AS LastScoredAt
            FROM canonical_values
            WHERE entity_id = @parentId
            ORDER BY key
            """, new { parentId = rootParentStr })
            .Select(r => new RegistryCanonicalValue(
                Key:               r.Key,
                Value:             r.Value,
                IsConflicted:      r.IsConflicted == 1,
                WinningProviderId: r.WinningProviderId,
                NeedsReview:       r.NeedsReview == 1,
                LastScoredAt:      DateTimeOffset.TryParse(r.LastScoredAt, out var dt) ? dt : DateTimeOffset.MinValue))
            .ToList();

        // Merge for the detail panel display: callers expect a single CanonicalValues
        // collection. Self values shadow same-key parent values for safety, but with
        // the partitioned writer there should be no overlap.
        var canonicalValues = parentValues
            .Where(p => !selfValues.Any(s => s.Key == p.Key))
            .Concat(selfValues)
            .ToList();

        if (canonicalValues.Count == 0)
            return Task.FromResult<RegistryItemDetail?>(null);

        // Load claim history from BOTH the asset row and the parent Work row,
        // since the writer partitions claims by scope.
        var claims = conn.Query<(string Id, string ClaimKey, string ClaimValue, string ProviderId,
            double Confidence, int IsUserLocked, string ClaimedAt)>("""
            SELECT id AS Id, claim_key AS ClaimKey, claim_value AS ClaimValue,
                   provider_id AS ProviderId, confidence AS Confidence,
                   is_user_locked AS IsUserLocked, claimed_at AS ClaimedAt
            FROM metadata_claims
            WHERE entity_id IN (@assetId, @parentId)
            ORDER BY claimed_at DESC
            """, new { assetId = assetIdStr, parentId = rootParentStr })
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
                WHEN 'RetailMatchFailed'     THEN 1
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
        var wikidataQid = cv(BridgeIdKeys.WikidataQid);

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
            BridgeIdKeys.Isbn, BridgeIdKeys.Isbn13, BridgeIdKeys.Isbn10, BridgeIdKeys.Asin, BridgeIdKeys.TmdbId, BridgeIdKeys.ImdbId, BridgeIdKeys.WikidataQid,
            BridgeIdKeys.AppleBooksId, BridgeIdKeys.AudibleId, BridgeIdKeys.GoodreadsId, BridgeIdKeys.MusicBrainzId,
            BridgeIdKeys.ComicVineId
        };
        var bridgeIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cvEntry in canonicalValues)
        {
            if (bridgeKeys.Contains(cvEntry.Key) && !string.IsNullOrWhiteSpace(cvEntry.Value))
                bridgeIds[cvEntry.Key] = cvEntry.Value;
        }
        if (!string.IsNullOrEmpty(wikidataQid) && !bridgeIds.ContainsKey(BridgeIdKeys.WikidataQid))
            bridgeIds[BridgeIdKeys.WikidataQid] = wikidataQid;

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
                 WHERE (ma.file_path_root LIKE '%/.data/staging/%'
                    OR ma.file_path_root LIKE '%\.data\staging\%')
                   AND NOT EXISTS (
                     SELECT 1 FROM review_queue rq
                     WHERE rq.entity_id = ma.id AND rq.status = 'Pending'
                   )) AS Staging,
                -- MissingImages: items with a wikidata_qid (Self, asset row) but no
                -- cover_url on the topmost Work row (Parent-scoped after Phase 4).
                (SELECT COUNT(DISTINCT e.work_id)
                 FROM editions e
                 JOIN media_assets ma ON ma.edition_id = e.id
                 JOIN works w  ON w.id = e.work_id
                 LEFT JOIN works p  ON p.id  = w.parent_work_id
                 LEFT JOIN works gp ON gp.id = p.parent_work_id
                 LEFT JOIN canonical_values cv_cover
                        ON cv_cover.entity_id = COALESCE(gp.id, p.id, w.id)
                       AND cv_cover.key = 'cover_url'
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
        // works.media_type is the authoritative source — no need to round-trip
        // through canonical_values for a column that lives directly on the row.
        // Only count playable leaf works (those with attached editions/media_assets);
        // parent container rows (albums, shows, series) are excluded.
        var rows = conn.Query<(string MediaType, int Count)>("""
            SELECT w.media_type AS MediaType, COUNT(DISTINCT w.id) AS Count
            FROM works w
            INNER JOIN editions e     ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            WHERE w.media_type IS NOT NULL AND w.media_type != ''
            GROUP BY w.media_type
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
