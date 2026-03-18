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
        id             AS Id,
        universe_id    AS UniverseId,
        display_name   AS DisplayName,
        created_at     AS CreatedAt,
        universe_status AS UniverseStatus,
        parent_hub_id  AS ParentHubId,
        wikidata_qid   AS WikidataQid,
        hub_type       AS HubType
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
                       h.hub_type,
                       w.id, w.media_type, w.sequence_index,
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
                        Id             = hubId,
                        UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
                        CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                        ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
                        HubType        = reader.IsDBNull(7) ? "Universe" : reader.GetString(7),
                    };
                    hubs[hubId] = hub;
                }

                // LEFT JOIN: work columns are NULL when the hub has no works.
                if (!reader.IsDBNull(8))
                {
                    var workId = Guid.Parse(reader.GetString(8));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hubId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(9), ignoreCase: true),
                            SequenceIndex      = reader.IsDBNull(10) ? null : reader.GetInt32(10),
                            UniverseMismatch   = !reader.IsDBNull(11) && reader.GetInt32(11) == 1,
                            UniverseMismatchAt = reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
                            WikidataStatus     = reader.IsDBNull(13) ? "pending" : reader.GetString(13),
                            WikidataCheckedAt  = reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)),
                            WikidataQid        = reader.IsDBNull(15) ? null : reader.GetString(15),
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
            INSERT OR IGNORE INTO hubs(id, universe_id, parent_hub_id, display_name, created_at, universe_status, wikidata_qid, hub_type)
                VALUES (@id, @uid, @phid, @dn, @ca, @us, @wqid, @ht);
            UPDATE hubs SET display_name = @dn, universe_status = @us, parent_hub_id = @phid,
                            wikidata_qid = @wqid, hub_type = @ht WHERE id = @id;
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

        total += conn.Execute("""
            DELETE FROM hubs
            WHERE id NOT IN (
                SELECT DISTINCT hub_id FROM works
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
        var hub = conn.QueryFirstOrDefault<Hub>($"""
            SELECT h.id             AS Id,
                   h.universe_id    AS UniverseId,
                   h.display_name   AS DisplayName,
                   h.created_at     AS CreatedAt,
                   h.universe_status AS UniverseStatus,
                   h.parent_hub_id  AS ParentHubId,
                   h.wikidata_qid   AS WikidataQid,
                   h.hub_type       AS HubType
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
}
