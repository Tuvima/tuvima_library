using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="IHubRepository"/>.
/// Loads all hubs with their child Works and each Work's CanonicalValues
/// using two sequential queries (no N+1) — same pattern as
/// <see cref="MediaAssetRepository"/>.
///
/// Uses Dapper for simple single-table queries; raw reader retained for the
/// complex multi-table JOIN in <see cref="GetAllAsync"/>.
/// </summary>
public sealed class HubRepository : IHubRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list for single-hub queries (no table prefix needed).
    private const string HubSelectColumns = """
        id                AS Id,
        universe_id       AS UniverseId,
        display_name      AS DisplayName,
        created_at        AS CreatedAt,
        universe_status   AS UniverseStatus,
        parent_hub_id     AS ParentHubId,
        wikidata_qid      AS WikidataQid,
        hub_type          AS HubType,
        description       AS Description,
        icon_name         AS IconName,
        scope             AS Scope,
        profile_id        AS ProfileId,
        is_enabled        AS IsEnabled,
        is_featured       AS IsFeatured,
        min_items         AS MinItems,
        rule_json         AS RuleJson,
        resolution        AS Resolution,
        rule_hash         AS RuleHash,
        group_by_field    AS GroupByField,
        match_mode        AS MatchMode,
        sort_field        AS SortField,
        sort_direction    AS SortDirection,
        live_updating     AS LiveUpdating,
        refresh_schedule  AS RefreshSchedule,
        last_refreshed_at AS LastRefreshedAt,
        modified_at       AS ModifiedAt
        """;

    // Reusable SELECT list for hub_relationships rows.
    private const string RelSelectColumns = """
        id            AS Id,
        hub_id        AS HubId,
        rel_type      AS RelType,
        rel_qid       AS RelQid,
        rel_label     AS RelLabel,
        confidence    AS Confidence,
        discovered_at AS DiscoveredAt
        """;

    public HubRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // Helpers — post-query fixup for Hub rows
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dapper maps nullable Guid columns to Guid? via the registered type handler,
    /// but UniverseStatus defaults to null when the DB column is NULL.
    /// This helper normalises defaults after Dapper mapping.
    /// </summary>
    private static Hub NormalizeHub(Hub h)
    {
        h.UniverseStatus ??= "Unknown";
        h.HubType ??= "Universe";
        h.Scope ??= "library";
        return h;
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — kept as raw reader (complex 3-query grouping pattern)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hub>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        var hubs  = new Dictionary<Guid, Hub>();
        var works = new Dictionary<Guid, Work>();

        // ── Query A: all hubs LEFT JOIN their works ───────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_hub_id, h.wikidata_qid,
                       h.hub_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   hubs h
                LEFT JOIN works w ON w.hub_id = h.id
                ORDER  BY h.created_at, w.id;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hubId = Guid.Parse(reader.GetString(0));
                if (!hubs.TryGetValue(hubId, out var hub))
                {
                    hub = new Hub
                    {
                        Id              = hubId,
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentHubId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        HubType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
                        Description     = reader.IsDBNull(8)  ? null : reader.GetString(8),
                        IconName        = reader.IsDBNull(9)  ? null : reader.GetString(9),
                        Scope           = reader.IsDBNull(10) ? "library" : reader.GetString(10),
                        ProfileId       = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                        IsEnabled       = !reader.IsDBNull(12) && reader.GetInt32(12) == 1,
                        IsFeatured      = !reader.IsDBNull(13) && reader.GetInt32(13) == 1,
                        MinItems        = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                        RuleJson        = reader.IsDBNull(15) ? null : reader.GetString(15),
                        RefreshSchedule = reader.IsDBNull(16) ? null : reader.GetString(16),
                        LastRefreshedAt = reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17)),
                        ModifiedAt      = reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
                    };
                    hubs[hubId] = hub;
                }

                // LEFT JOIN: work columns are NULL when the hub has no works.
                if (!reader.IsDBNull(19))
                {
                    var workId = Guid.Parse(reader.GetString(19));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hubId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                            Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                            UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                            UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                            WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                            WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                            WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                        };
                        works[workId] = work;
                        hub.Works.Add(work);
                    }
                }
            }
        }

        // ── Query B: canonical values for all loaded works ────────────────────
        if (works.Count > 0)
        {
            var workIds    = works.Keys.ToList();
            var paramNames = workIds.Select((_, i) => $"@p{i}").ToList();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = $"""
                SELECT e.work_id, cv.entity_id, cv.key, cv.value, cv.last_scored_at
                FROM   canonical_values cv
                JOIN   media_assets ma ON ma.id = cv.entity_id
                JOIN   editions e      ON e.id  = ma.edition_id
                WHERE  e.work_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < workIds.Count; i++)
                cmd2.Parameters.AddWithValue($"@p{i}", workIds[i].ToString());

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var workId   = Guid.Parse(reader2.GetString(0));
                var entityId = Guid.Parse(reader2.GetString(1));
                if (works.TryGetValue(workId, out var work))
                {
                    work.CanonicalValues.Add(new CanonicalValue
                    {
                        EntityId     = entityId,
                        Key          = reader2.GetString(2),
                        Value        = reader2.GetString(3),
                        LastScoredAt = DateTimeOffset.Parse(reader2.GetString(4)),
                    });
                }
            }
        }

        // ── Query C: hub relationships ────────────────────────────────────────
        if (hubs.Count > 0)
        {
            var hubIds     = hubs.Keys.ToList();
            var paramNames = hubIds.Select((_, i) => $"@h{i}").ToList();

            using var cmd3 = conn.CreateCommand();
            cmd3.CommandText = $"""
                SELECT id, hub_id, rel_type, rel_qid, rel_label, confidence, discovered_at
                FROM   hub_relationships
                WHERE  hub_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < hubIds.Count; i++)
                cmd3.Parameters.AddWithValue($"@h{i}", hubIds[i].ToString());

            using var reader3 = cmd3.ExecuteReader();
            while (reader3.Read())
            {
                var hubId = Guid.Parse(reader3.GetString(1));
                if (hubs.TryGetValue(hubId, out var hub))
                {
                    hub.Relationships.Add(new HubRelationship
                    {
                        Id           = Guid.Parse(reader3.GetString(0)),
                        HubId        = hubId,
                        RelType      = reader3.GetString(2),
                        RelQid       = reader3.GetString(3),
                        RelLabel     = reader3.IsDBNull(4) ? null : reader3.GetString(4),
                        Confidence   = reader3.GetDouble(5),
                        DiscoveredAt = DateTimeOffset.Parse(reader3.GetString(6)),
                    });
                }
            }
        }

        IReadOnlyList<Hub> result = hubs.Values.ToList();
        return Task.FromResult(result);
    }

    // -------------------------------------------------------------------------
    // Single-hub read methods — converted to Dapper
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<Hub?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // Find the hub that owns a relationship matching (rel_type, rel_qid).
        var hubIdStr = conn.ExecuteScalar<string>("""
            SELECT hub_id FROM hub_relationships
            WHERE  rel_type = @relType AND rel_qid = @qid
            LIMIT  1;
            """, new { relType, qid });

        if (hubIdStr is null)
            return Task.FromResult<Hub?>(null);

        var hubId = Guid.Parse(hubIdStr);

        var hub = conn.QueryFirstOrDefault<Hub>($"""
            SELECT {HubSelectColumns}
            FROM   hubs WHERE id = @id;
            """, new { id = hubId.ToString() });

        if (hub is null)
            return Task.FromResult<Hub?>(null);

        NormalizeHub(hub);

        // Load relationships for this hub.
        var rels = conn.Query<HubRelationship>($"""
            SELECT {RelSelectColumns}
            FROM   hub_relationships WHERE hub_id = @hid;
            """, new { hid = hubId.ToString() }).AsList();

        hub.Relationships.AddRange(rels);

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public async Task InsertRelationshipsAsync(IReadOnlyList<HubRelationship> relationships, CancellationToken ct = default)
    {
        if (relationships.Count == 0) return;
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx   = conn.BeginTransaction();

            const string sql = """
                INSERT OR IGNORE INTO hub_relationships (id, hub_id, rel_type, rel_qid, rel_label, confidence, discovered_at)
                VALUES (@id, @hubId, @relType, @relQid, @relLabel, @confidence, @discoveredAt);
                """;

            var rows = relationships.Select(r => new
            {
                id           = r.Id.ToString(),
                hubId        = r.HubId.ToString(),
                r.RelType,
                r.RelQid,
                relLabel     = r.RelLabel,
                r.Confidence,
                discoveredAt = r.DiscoveredAt.ToString("O"),
            });

            conn.Execute(sql, rows, transaction: tx);
            tx.Commit();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<Guid?> GetWorkIdByMediaAssetAsync(Guid mediaAssetId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string>("""
            SELECT e.work_id
            FROM   media_assets ma
            JOIN   editions e ON e.id = ma.edition_id
            WHERE  ma.id = @assetId
            LIMIT  1;
            """, new { assetId = mediaAssetId.ToString() });

        return Task.FromResult(result is null ? null : (Guid?)Guid.Parse(result));
    }

    /// <inheritdoc/>
    public Task<string?> FindHubNameByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string>("""
            SELECT h.display_name
            FROM   works w
            JOIN   hubs h ON h.id = w.hub_id
            WHERE  w.id = @workId
            LIMIT  1;
            """, new { workId = workId.ToString() });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task AssignWorkToHubAsync(Guid workId, Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE works SET hub_id = @hubId WHERE id = @workId;",
                new { hubId = hubId.ToString(), workId = workId.ToString() });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public async Task MergeHubsAsync(Guid keepHubId, Guid mergeHubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            var keep  = keepHubId.ToString();
            var merge = mergeHubId.ToString();

            using var conn = _db.CreateConnection();
            using var tx   = conn.BeginTransaction();

            // Re-assign all Works from mergeHub to keepHub.
            conn.Execute(
                "UPDATE works SET hub_id = @keep WHERE hub_id = @merge;",
                new { keep, merge }, transaction: tx);

            // Move all relationships from mergeHub to keepHub (ignore duplicates).
            conn.Execute(
                "UPDATE OR IGNORE hub_relationships SET hub_id = @keep WHERE hub_id = @merge;",
                new { keep, merge }, transaction: tx);

            // Delete any remaining relationships on the merged hub (duplicates that couldn't move).
            conn.Execute(
                "DELETE FROM hub_relationships WHERE hub_id = @merge;",
                new { merge }, transaction: tx);

            // Delete the merged hub.
            conn.Execute(
                "DELETE FROM hubs WHERE id = @merge;",
                new { merge }, transaction: tx);

            tx.Commit();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<Hub?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var hub = conn.QueryFirstOrDefault<Hub>($"""
            SELECT {HubSelectColumns}
            FROM   hubs
            WHERE  LOWER(display_name) = LOWER(@name)
            LIMIT  1;
            """, new { name = displayName });

        return Task.FromResult(hub is null ? null : (Hub?)NormalizeHub(hub));
    }

    /// <inheritdoc/>
    public Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT OR IGNORE INTO hubs(id, universe_id, parent_hub_id, display_name, created_at,
                universe_status, wikidata_qid, hub_type, description, icon_name, scope, profile_id,
                is_enabled, is_featured, min_items, rule_json, resolution, rule_hash,
                group_by_field, match_mode, sort_field, sort_direction, live_updating)
                VALUES (@id, @uid, @phid, @dn, @ca, @us, @wqid, @ht, @desc, @icon, @scope, @pid,
                    @enabled, @featured, @minItems, @ruleJson, @resolution, @ruleHash,
                    @groupByField, @matchMode, @sortField, @sortDirection, @liveUpdating);
            UPDATE hubs SET display_name = @dn, universe_status = @us, parent_hub_id = @phid,
                            wikidata_qid = @wqid, hub_type = @ht, description = @desc,
                            icon_name = @icon, scope = @scope, profile_id = @pid,
                            is_enabled = @enabled, is_featured = @featured, min_items = @minItems,
                            rule_json = @ruleJson, resolution = @resolution, rule_hash = @ruleHash,
                            group_by_field = @groupByField, match_mode = @matchMode,
                            sort_field = @sortField, sort_direction = @sortDirection,
                            live_updating = @liveUpdating
                    WHERE id = @id;
            """,
            new
            {
                id   = hub.Id.ToString(),
                uid  = hub.UniverseId.HasValue ? hub.UniverseId.Value.ToString() : null,
                phid = hub.ParentHubId.HasValue ? hub.ParentHubId.Value.ToString() : null,
                dn   = hub.DisplayName,
                ca   = hub.CreatedAt.ToString("O"),
                us   = hub.UniverseStatus ?? "Unknown",
                wqid = hub.WikidataQid,
                ht   = hub.HubType ?? "Universe",
                desc = hub.Description,
                icon = hub.IconName,
                scope = hub.Scope ?? "library",
                pid  = hub.ProfileId.HasValue ? hub.ProfileId.Value.ToString() : null,
                enabled = hub.IsEnabled ? 1 : 0,
                featured = hub.IsFeatured ? 1 : 0,
                minItems = hub.MinItems,
                ruleJson = hub.RuleJson,
                resolution = hub.Resolution ?? "query",
                ruleHash = hub.RuleHash,
                groupByField = hub.GroupByField,
                matchMode = hub.MatchMode ?? "all",
                sortField = hub.SortField,
                sortDirection = hub.SortDirection ?? "desc",
                liveUpdating = hub.LiveUpdating ? 1 : 0,
            });

        return Task.FromResult(hub.Id);
    }

    /// <inheritdoc/>
    public Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE works
            SET    universe_mismatch    = 1,
                   universe_mismatch_at = @now
            WHERE  id = @id;
            """,
            new
            {
                id  = workId.ToString(),
                now = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateWorkWikidataStatusAsync(Guid workId, string status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            UPDATE works
            SET    wikidata_status     = @status,
                   wikidata_checked_at = @now
            WHERE  id = @id;
            """,
            new
            {
                id     = workId.ToString(),
                status,
                now    = DateTimeOffset.UtcNow.ToString("O"),
            });

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PruneOrphanedHierarchyAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        int total = 0;

        // Pass 1: Remove editions that have no media assets.
        total += conn.Execute("""
            DELETE FROM editions
            WHERE id NOT IN (
                SELECT DISTINCT edition_id FROM media_assets
            );
            """);

        // Pass 2: Remove works that have no editions (after pass 1).
        total += conn.Execute("""
            DELETE FROM works
            WHERE id NOT IN (
                SELECT DISTINCT work_id FROM editions
            );
            """);

        // Pass 3: Remove hubs that have no works (after pass 2),
        // including their child hub_relationships rows.
        conn.Execute("""
            DELETE FROM hub_relationships
            WHERE hub_id NOT IN (
                SELECT DISTINCT hub_id FROM works
            );
            """); // relationships are not counted in the total

        // Only prune ContentGroup / Universe hubs — these are the ones whose
        // identity is bound to the works they contain. System view hubs (e.g.
        // "Music by Album", "TV by Show"), Smart hubs, Mix, Playlist, and Custom
        // hubs are query-resolved and intentionally have no work rows; they must
        // never be pruned.
        total += conn.Execute("""
            DELETE FROM hubs
            WHERE hub_type IN ('ContentGroup', 'Universe')
              AND id NOT IN (
                SELECT DISTINCT hub_id FROM works WHERE hub_id IS NOT NULL
            );
            """);

        return Task.FromResult(total);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hub>> GetChildHubsAsync(Guid parentHubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<Hub>($"""
            SELECT {HubSelectColumns}
            FROM   hubs
            WHERE  parent_hub_id = @parentHubId
            ORDER  BY display_name;
            """, new { parentHubId = parentHubId.ToString() })
            .Select(NormalizeHub)
            .ToList();

        return Task.FromResult<IReadOnlyList<Hub>>(results);
    }

    /// <inheritdoc/>
    public async Task SetParentHubAsync(Guid hubId, Guid? parentHubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE hubs SET parent_hub_id = @parentHubId WHERE id = @id;",
                new
                {
                    parentHubId = parentHubId.HasValue ? parentHubId.Value.ToString() : null,
                    id          = hubId.ToString(),
                });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<Hub?> FindParentHubByRelationshipAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var hub = conn.QueryFirstOrDefault<Hub>("""
            SELECT h.id                AS Id,
                   h.universe_id       AS UniverseId,
                   h.display_name      AS DisplayName,
                   h.created_at        AS CreatedAt,
                   h.universe_status   AS UniverseStatus,
                   h.parent_hub_id     AS ParentHubId,
                   h.wikidata_qid      AS WikidataQid,
                   h.hub_type          AS HubType,
                   h.description       AS Description,
                   h.icon_name         AS IconName,
                   h.scope             AS Scope,
                   h.profile_id        AS ProfileId,
                   h.is_enabled        AS IsEnabled,
                   h.is_featured       AS IsFeatured,
                   h.min_items         AS MinItems,
                   h.rule_json         AS RuleJson,
                   h.refresh_schedule  AS RefreshSchedule,
                   h.last_refreshed_at AS LastRefreshedAt,
                   h.modified_at       AS ModifiedAt
            FROM   hubs h
            INNER JOIN hub_relationships hr ON hr.hub_id = h.id
            WHERE  hr.rel_qid = @qid
              AND  hr.rel_type IN ('franchise', 'fictional_universe')
              AND  h.parent_hub_id IS NULL
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(hub is null ? null : (Hub?)NormalizeHub(hub));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> FindHubIdsByFranchiseQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<string>("""
            SELECT DISTINCT hub_id
            FROM   hub_relationships
            WHERE  rel_qid  = @qid
              AND  rel_type IN ('franchise', 'fictional_universe');
            """, new { qid })
            .Select(Guid.Parse)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HubRelationship>> GetRelationshipsAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<HubRelationship>($"""
            SELECT {RelSelectColumns}
            FROM   hub_relationships
            WHERE  hub_id = @hubId;
            """, new { hubId = hubId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<HubRelationship>>(results);
    }

    /// <inheritdoc/>
    public Task<Hub?> GetByIdAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var hub = conn.QueryFirstOrDefault<Hub>($"""
            SELECT {HubSelectColumns}
            FROM   hubs
            WHERE  id = @id
            LIMIT  1;
            """, new { id = hubId.ToString() });

        return Task.FromResult(hub is null ? null : (Hub?)NormalizeHub(hub));
    }

    /// <inheritdoc/>
    public Task<Hub?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var hub = conn.QueryFirstOrDefault<Hub>($"""
            SELECT {HubSelectColumns}
            FROM   hubs
            WHERE  wikidata_qid = @qid
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(hub is null ? null : (Hub?)NormalizeHub(hub));
    }

    /// <inheritdoc/>
    public Task<Edition?> FindEditionByQidAsync(string wikidataQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd  = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, work_id, format_label, wikidata_qid
            FROM   editions
            WHERE  wikidata_qid = @qid
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", wikidataQid);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Edition?>(null);

        var edition = new Edition
        {
            Id          = Guid.Parse(reader.GetString(0)),
            WorkId      = Guid.Parse(reader.GetString(1)),
            FormatLabel = reader.IsDBNull(2) ? null : reader.GetString(2),
            WikidataQid = reader.IsDBNull(3) ? null : reader.GetString(3),
        };

        return Task.FromResult<Edition?>(edition);
    }

    /// <inheritdoc/>
    public async Task<Edition> CreateEditionAsync(Guid workId, string? formatLabel, string? wikidataQid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var edition = new Edition
        {
            Id          = Guid.NewGuid(),
            WorkId      = workId,
            FormatLabel = formatLabel,
            WikidataQid = wikidataQid,
        };

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute("""
                INSERT INTO editions (id, work_id, format_label, wikidata_qid)
                VALUES (@id, @workId, @formatLabel, @wikidataQid);
                """,
                new
                {
                    id          = edition.Id.ToString(),
                    workId      = edition.WorkId.ToString(),
                    formatLabel = edition.FormatLabel,
                    wikidataQid = edition.WikidataQid,
                });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }

        return edition;
    }

    /// <inheritdoc/>
    public async Task UpdateMatchLevelAsync(Guid workId, string matchLevel, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE works SET match_level = @matchLevel WHERE id = @workId;",
                new { matchLevel, workId = workId.ToString() });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    // ── Managed Hub methods ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Hub>> GetByTypeAsync(string hubType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var hubs = (await conn.QueryAsync<Hub>(
            $"SELECT {HubSelectColumns} FROM hubs WHERE hub_type = @HubType ORDER BY display_name",
            new { HubType = hubType })).ToList();
        hubs.ForEach(h => NormalizeHub(h));
        return hubs;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Hub>> GetManagedHubsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var hubs = (await conn.QueryAsync<Hub>(
            $"SELECT {HubSelectColumns} FROM hubs WHERE hub_type != 'Universe' ORDER BY hub_type, display_name")).ToList();
        hubs.ForEach(h => NormalizeHub(h));
        return hubs;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<(string HubType, int Count)>(
            "SELECT hub_type AS HubType, COUNT(*) AS Count FROM hubs WHERE hub_type != 'Universe' GROUP BY hub_type");
        return rows.ToDictionary(r => r.HubType, r => r.Count);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HubItem>> GetHubItemsAsync(Guid hubId, int limit = 20, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var items = await conn.QueryAsync<HubItem>(
            """
            SELECT id AS Id, hub_id AS HubId, work_id AS WorkId,
                   sort_order AS SortOrder, progress_state AS ProgressState,
                   progress_position AS ProgressPosition, added_at AS AddedAt
            FROM hub_items WHERE hub_id = @HubId
            ORDER BY sort_order LIMIT @Limit
            """,
            new { HubId = hubId.ToString(), Limit = limit });
        return items.ToList();
    }

    /// <inheritdoc/>
    public async Task<int> GetHubItemCountAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM hub_items WHERE hub_id = @HubId",
            new { HubId = hubId.ToString() });
    }

    /// <inheritdoc/>
    public async Task UpdateHubEnabledAsync(Guid hubId, bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE hubs SET is_enabled = @Enabled, modified_at = datetime('now') WHERE id = @Id",
            new { Id = hubId.ToString(), Enabled = enabled ? 1 : 0 });
    }

    /// <inheritdoc/>
    public async Task UpdateHubFeaturedAsync(Guid hubId, bool featured, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE hubs SET is_featured = @Featured, modified_at = datetime('now') WHERE id = @Id",
            new { Id = hubId.ToString(), Featured = featured ? 1 : 0 });
    }

    /// <inheritdoc/>
    public async Task AddHubItemAsync(HubItem item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO hub_items (id, hub_id, work_id, sort_order, progress_state, progress_position, added_at)
            VALUES (@Id, @HubId, @WorkId, @SortOrder, @ProgressState, @ProgressPosition, @AddedAt)
            """,
            new
            {
                Id = (item.Id == Guid.Empty ? Guid.NewGuid() : item.Id).ToString(),
                HubId = item.HubId.ToString(),
                WorkId = item.WorkId.ToString(),
                item.SortOrder,
                item.ProgressState,
                item.ProgressPosition,
                AddedAt = item.AddedAt == default ? DateTimeOffset.UtcNow.ToString("o") : item.AddedAt.ToString("o")
            });
    }

    /// <inheritdoc/>
    public async Task RemoveHubItemAsync(Guid itemId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM hub_items WHERE id = @Id",
            new { Id = itemId.ToString() });
    }

    // -------------------------------------------------------------------------
    // Content Groups — Universe hubs that contain works (albums, series, etc.)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hub>> GetContentGroupsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        var hubs  = new Dictionary<Guid, Hub>();
        var works = new Dictionary<Guid, Work>();

        // Universe hubs that have at least one work assigned.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_hub_id, h.wikidata_qid,
                       h.hub_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   hubs h
                INNER JOIN works w ON w.hub_id = h.id
                WHERE  h.hub_type IN ('ContentGroup', 'Universe')
                ORDER  BY h.display_name, h.created_at, w.ordinal, w.id;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var hubId = Guid.Parse(reader.GetString(0));
                if (!hubs.TryGetValue(hubId, out var hub))
                {
                    hub = new Hub
                    {
                        Id              = hubId,
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentHubId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        HubType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
                        Description     = reader.IsDBNull(8)  ? null : reader.GetString(8),
                        IconName        = reader.IsDBNull(9)  ? null : reader.GetString(9),
                        Scope           = reader.IsDBNull(10) ? "library" : reader.GetString(10),
                        ProfileId       = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                        IsEnabled       = !reader.IsDBNull(12) && reader.GetInt32(12) == 1,
                        IsFeatured      = !reader.IsDBNull(13) && reader.GetInt32(13) == 1,
                        MinItems        = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                        RuleJson        = reader.IsDBNull(15) ? null : reader.GetString(15),
                        RefreshSchedule = reader.IsDBNull(16) ? null : reader.GetString(16),
                        LastRefreshedAt = reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17)),
                        ModifiedAt      = reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
                    };
                    hubs[hubId] = hub;
                }

                var workId = Guid.Parse(reader.GetString(19));
                if (!works.ContainsKey(workId))
                {
                    var work = new Work
                    {
                        Id                 = workId,
                        HubId              = hubId,
                        MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                        Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                        UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                        UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                        WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                        WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                        WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                    };
                    works[workId] = work;
                    hub.Works.Add(work);
                }
            }
        }

        // Canonical values for all loaded works.
        if (works.Count > 0)
        {
            var workIds    = works.Keys.ToList();
            var paramNames = workIds.Select((_, i) => $"@p{i}").ToList();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = $"""
                SELECT e.work_id, cv.entity_id, cv.key, cv.value, cv.last_scored_at
                FROM   canonical_values cv
                JOIN   media_assets ma ON ma.id = cv.entity_id
                JOIN   editions e      ON e.id  = ma.edition_id
                WHERE  e.work_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < workIds.Count; i++)
                cmd2.Parameters.AddWithValue($"@p{i}", workIds[i].ToString());

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var workId   = Guid.Parse(reader2.GetString(0));
                if (works.TryGetValue(workId, out var work))
                {
                    work.CanonicalValues.Add(new CanonicalValue
                    {
                        EntityId     = Guid.Parse(reader2.GetString(1)),
                        Key          = reader2.GetString(2),
                        Value        = reader2.GetString(3),
                        LastScoredAt = DateTimeOffset.Parse(reader2.GetString(4)),
                    });
                }
            }
        }

        IReadOnlyList<Hub> result = hubs.Values.ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Hub?> GetHubWithWorksAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        Hub? hub       = null;
        var  works     = new Dictionary<Guid, Work>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_hub_id, h.wikidata_qid,
                       h.hub_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   hubs h
                LEFT JOIN works w ON w.hub_id = h.id
                WHERE  h.id = @HubId
                ORDER  BY w.ordinal, w.id;
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@HubId";
            p.Value = hubId.ToString();
            cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (hub is null)
                {
                    hub = new Hub
                    {
                        Id              = Guid.Parse(reader.GetString(0)),
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentHubId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        HubType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
                        Description     = reader.IsDBNull(8)  ? null : reader.GetString(8),
                        IconName        = reader.IsDBNull(9)  ? null : reader.GetString(9),
                        Scope           = reader.IsDBNull(10) ? "library" : reader.GetString(10),
                        ProfileId       = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                        IsEnabled       = !reader.IsDBNull(12) && reader.GetInt32(12) == 1,
                        IsFeatured      = !reader.IsDBNull(13) && reader.GetInt32(13) == 1,
                        MinItems        = reader.IsDBNull(14) ? 0 : reader.GetInt32(14),
                        RuleJson        = reader.IsDBNull(15) ? null : reader.GetString(15),
                        RefreshSchedule = reader.IsDBNull(16) ? null : reader.GetString(16),
                        LastRefreshedAt = reader.IsDBNull(17) ? null : DateTimeOffset.Parse(reader.GetString(17)),
                        ModifiedAt      = reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
                    };
                }

                // LEFT JOIN: work columns are NULL when hub has no works.
                if (!reader.IsDBNull(19))
                {
                    var workId = Guid.Parse(reader.GetString(19));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hub.Id,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                            Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                            UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                            UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                            WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                            WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                            WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                        };
                        works[workId] = work;
                        hub.Works.Add(work);
                    }
                }
            }
        }

        if (hub is null)
            return Task.FromResult<Hub?>(null);

        // Canonical values for all loaded works.
        if (works.Count > 0)
        {
            var workIds    = works.Keys.ToList();
            var paramNames = workIds.Select((_, i) => $"@p{i}").ToList();

            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = $"""
                SELECT e.work_id, cv.entity_id, cv.key, cv.value, cv.last_scored_at
                FROM   canonical_values cv
                JOIN   media_assets ma ON ma.id = cv.entity_id
                JOIN   editions e      ON e.id  = ma.edition_id
                WHERE  e.work_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < workIds.Count; i++)
                cmd2.Parameters.AddWithValue($"@p{i}", workIds[i].ToString());

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var wid = Guid.Parse(reader2.GetString(0));
                if (works.TryGetValue(wid, out var work))
                {
                    work.CanonicalValues.Add(new CanonicalValue
                    {
                        EntityId     = Guid.Parse(reader2.GetString(1)),
                        Key          = reader2.GetString(2),
                        Value        = reader2.GetString(3),
                        LastScoredAt = DateTimeOffset.Parse(reader2.GetString(4)),
                    });
                }
            }
        }

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<Guid?> GetHubIdByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var raw = conn.QueryFirstOrDefault<string>(
            "SELECT hub_id FROM works WHERE id = @workId AND hub_id IS NOT NULL;",
            new { workId = workId.ToString() });

        if (raw is null) return Task.FromResult<Guid?>(null);
        return Task.FromResult<Guid?>(Guid.Parse(raw));
    }

    /// <inheritdoc/>
    public async Task<Hub?> FindByRuleHashAsync(string ruleHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var hub = await conn.QueryFirstOrDefaultAsync<Hub>(
            $"SELECT {HubSelectColumns} FROM hubs WHERE rule_hash = @Hash LIMIT 1",
            new { Hash = ruleHash });
        return hub is null ? null : NormalizeHub(hub);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Hub>> GetAllHubsForLocationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var hubs = await conn.QueryAsync<Hub>(
            $"SELECT {HubSelectColumns} FROM hubs WHERE is_enabled = 1 ORDER BY display_name");
        return hubs.Select(NormalizeHub).ToList();
    }
}
