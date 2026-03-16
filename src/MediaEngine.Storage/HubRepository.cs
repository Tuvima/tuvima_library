using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// ORM-less SQLite implementation of <see cref="IHubRepository"/>.
/// Loads all hubs with their child Works and each Work's CanonicalValues
/// using two sequential queries (no N+1) — same pattern as
/// <see cref="MediaAssetRepository"/>.
/// </summary>
public sealed class HubRepository : IHubRepository
{
    private readonly IDatabaseConnection _db;

    public HubRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

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
                    };
                    hubs[hubId] = hub;
                }

                // LEFT JOIN: work columns are NULL when the hub has no works.
                // h.wikidata_qid is at ordinal 6; w.id starts at ordinal 7.
                if (!reader.IsDBNull(7))
                {
                    var workId = Guid.Parse(reader.GetString(7));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hubId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(8), ignoreCase: true),
                            SequenceIndex      = reader.IsDBNull(9) ? null : reader.GetInt32(9),
                            UniverseMismatch   = !reader.IsDBNull(10) && reader.GetInt32(10) == 1,
                            UniverseMismatchAt = reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
                            WikidataStatus     = reader.IsDBNull(12) ? "pending" : reader.GetString(12),
                            WikidataCheckedAt  = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)),
                            WikidataQid        = reader.IsDBNull(14) ? null : reader.GetString(14),
                        };
                        works[workId] = work;
                        hub.Works.Add(work);
                    }
                }
            }
        }

        // ── Query B: canonical values for all loaded works ────────────────────
        // Canonical values are stored with EntityId = media_asset.id (the asset
        // that was scored), not the work.id.  We join through editions →
        // media_assets to resolve the correct asset IDs, then project the
        // work_id so we can attach each value to the right Work object.
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

    /// <inheritdoc/>
    public Task<Hub?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // Find the hub that owns a relationship matching (rel_type, rel_qid).
        Guid? hubId = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT hub_id FROM hub_relationships
                WHERE  rel_type = @relType AND rel_qid = @qid
                LIMIT  1;
                """;
            cmd.Parameters.AddWithValue("@relType", relType);
            cmd.Parameters.AddWithValue("@qid", qid);
            var result = cmd.ExecuteScalar();
            if (result is string s)
                hubId = Guid.Parse(s);
        }

        if (hubId is null)
            return Task.FromResult<Hub?>(null);

        // Load the Hub with its relationships.
        using var hubCmd = conn.CreateCommand();
        hubCmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status, parent_hub_id, wikidata_qid
            FROM   hubs WHERE id = @id;
            """;
        hubCmd.Parameters.AddWithValue("@id", hubId.Value.ToString());

        Hub? hub = null;
        using (var reader = hubCmd.ExecuteReader())
        {
            if (reader.Read())
            {
                hub = new Hub
                {
                    Id             = Guid.Parse(reader.GetString(0)),
                    UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                    DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
                    CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
                    UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                    ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                    WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
                };
            }
        }

        if (hub is null)
            return Task.FromResult<Hub?>(null);

        // Load relationships for this hub.
        using var relCmd = conn.CreateCommand();
        relCmd.CommandText = """
            SELECT id, hub_id, rel_type, rel_qid, rel_label, confidence, discovered_at
            FROM   hub_relationships WHERE hub_id = @hid;
            """;
        relCmd.Parameters.AddWithValue("@hid", hubId.Value.ToString());
        using var relReader = relCmd.ExecuteReader();
        while (relReader.Read())
        {
            hub.Relationships.Add(new HubRelationship
            {
                Id           = Guid.Parse(relReader.GetString(0)),
                HubId        = hub.Id,
                RelType      = relReader.GetString(2),
                RelQid       = relReader.GetString(3),
                RelLabel     = relReader.IsDBNull(4) ? null : relReader.GetString(4),
                Confidence   = relReader.GetDouble(5),
                DiscoveredAt = DateTimeOffset.Parse(relReader.GetString(6)),
            });
        }

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
            using var tx = conn.BeginTransaction();

            foreach (var rel in relationships)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT OR IGNORE INTO hub_relationships (id, hub_id, rel_type, rel_qid, rel_label, confidence, discovered_at)
                    VALUES (@id, @hubId, @relType, @relQid, @relLabel, @confidence, @discoveredAt);
                    """;
                cmd.Parameters.AddWithValue("@id", rel.Id.ToString());
                cmd.Parameters.AddWithValue("@hubId", rel.HubId.ToString());
                cmd.Parameters.AddWithValue("@relType", rel.RelType);
                cmd.Parameters.AddWithValue("@relQid", rel.RelQid);
                cmd.Parameters.AddWithValue("@relLabel", rel.RelLabel ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@confidence", rel.Confidence);
                cmd.Parameters.AddWithValue("@discoveredAt", rel.DiscoveredAt.ToString("O"));
                cmd.ExecuteNonQuery();
            }

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.work_id
            FROM   media_assets ma
            JOIN   editions e ON e.id = ma.edition_id
            WHERE  ma.id = @assetId
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@assetId", mediaAssetId.ToString());

        using var reader = cmd.ExecuteReader();
        Guid? result = null;
        if (reader.Read())
            result = Guid.Parse(reader.GetString(0));

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<string?> FindHubNameByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.display_name
            FROM   works w
            JOIN   hubs h ON h.id = w.hub_id
            WHERE  w.id = @workId
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@workId", workId.ToString());

        using var reader = cmd.ExecuteReader();
        string? result = null;
        if (reader.Read() && !reader.IsDBNull(0))
            result = reader.GetString(0);

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
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE works SET hub_id = @hubId WHERE id = @workId;
                """;
            cmd.Parameters.AddWithValue("@hubId", hubId.ToString());
            cmd.Parameters.AddWithValue("@workId", workId.ToString());
            cmd.ExecuteNonQuery();
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
            using var conn = _db.CreateConnection();
            using var tx = conn.BeginTransaction();

            // Re-assign all Works from mergeHub to keepHub.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE works SET hub_id = @keep WHERE hub_id = @merge;";
                cmd.Parameters.AddWithValue("@keep", keepHubId.ToString());
                cmd.Parameters.AddWithValue("@merge", mergeHubId.ToString());
                cmd.ExecuteNonQuery();
            }

            // Move all relationships from mergeHub to keepHub (ignore duplicates).
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE OR IGNORE hub_relationships SET hub_id = @keep WHERE hub_id = @merge;";
                cmd.Parameters.AddWithValue("@keep", keepHubId.ToString());
                cmd.Parameters.AddWithValue("@merge", mergeHubId.ToString());
                cmd.ExecuteNonQuery();
            }

            // Delete any remaining relationships on the merged hub (duplicates that couldn't move).
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM hub_relationships WHERE hub_id = @merge;";
                cmd.Parameters.AddWithValue("@merge", mergeHubId.ToString());
                cmd.ExecuteNonQuery();
            }

            // Delete the merged hub.
            using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM hubs WHERE id = @merge;";
                cmd.Parameters.AddWithValue("@merge", mergeHubId.ToString());
                cmd.ExecuteNonQuery();
            }

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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status, parent_hub_id, wikidata_qid
            FROM   hubs
            WHERE  LOWER(display_name) = LOWER(@name)
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@name", displayName);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Hub?>(null);

        var hub = new Hub
        {
            Id             = Guid.Parse(reader.GetString(0)),
            UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
            UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
            ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
        };

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // INSERT OR IGNORE ensures idempotency for new hubs.
        // UPDATE sets display_name and parent_hub_id on every call so the latest
        // ingested values win.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO hubs(id, universe_id, parent_hub_id, display_name, created_at, universe_status, wikidata_qid)
                VALUES (@id, @uid, @phid, @dn, @ca, @us, @wqid);
            UPDATE hubs SET display_name = @dn, universe_status = @us, parent_hub_id = @phid,
                            wikidata_qid = @wqid WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",   hub.Id.ToString());
        cmd.Parameters.AddWithValue("@uid",  hub.UniverseId.HasValue
            ? hub.UniverseId.Value.ToString()
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@phid", hub.ParentHubId.HasValue
            ? hub.ParentHubId.Value.ToString()
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@dn",   hub.DisplayName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca",   hub.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@us",   hub.UniverseStatus ?? "Unknown");
        cmd.Parameters.AddWithValue("@wqid", (object?)hub.WikidataQid ?? DBNull.Value);
        cmd.ExecuteNonQuery();

        return Task.FromResult(hub.Id);
    }

    /// <inheritdoc/>
    public Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE works
            SET    universe_mismatch    = 1,
                   universe_mismatch_at = @now
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",  workId.ToString());
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpdateWorkWikidataStatusAsync(Guid workId, string status, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE works
            SET    wikidata_status     = @status,
                   wikidata_checked_at = @now
            WHERE  id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",     workId.ToString());
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@now",    DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<int> PruneOrphanedHierarchyAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        int total = 0;

        // Pass 1: Remove editions that have no media assets.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM editions
                WHERE id NOT IN (
                    SELECT DISTINCT edition_id FROM media_assets
                );
                """;
            total += cmd.ExecuteNonQuery();
        }

        // Pass 2: Remove works that have no editions (after pass 1).
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM works
                WHERE id NOT IN (
                    SELECT DISTINCT work_id FROM editions
                );
                """;
            total += cmd.ExecuteNonQuery();
        }

        // Pass 3: Remove hubs that have no works (after pass 2),
        // including their child hub_relationships rows.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM hub_relationships
                WHERE hub_id NOT IN (
                    SELECT DISTINCT hub_id FROM works
                );
                """;
            cmd.ExecuteNonQuery(); // relationships are not counted in the total
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                DELETE FROM hubs
                WHERE id NOT IN (
                    SELECT DISTINCT hub_id FROM works
                );
                """;
            total += cmd.ExecuteNonQuery();
        }

        return Task.FromResult(total);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Hub>> GetChildHubsAsync(Guid parentHubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status, parent_hub_id, wikidata_qid
            FROM   hubs
            WHERE  parent_hub_id = @parentHubId
            ORDER  BY display_name;
            """;
        cmd.Parameters.AddWithValue("@parentHubId", parentHubId.ToString());

        var results = new List<Hub>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new Hub
            {
                Id             = Guid.Parse(reader.GetString(0)),
                UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
                CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
                UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
                ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
            });
        }

        IReadOnlyList<Hub> result = results;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task SetParentHubAsync(Guid hubId, Guid? parentHubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE hubs SET parent_hub_id = @parentHubId WHERE id = @id;
                """;
            cmd.Parameters.AddWithValue("@parentHubId", parentHubId.HasValue
                ? parentHubId.Value.ToString()
                : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@id", hubId.ToString());
            cmd.ExecuteNonQuery();
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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT h.id, h.universe_id, h.display_name, h.created_at, h.universe_status, h.parent_hub_id, h.wikidata_qid
            FROM   hubs h
            INNER JOIN hub_relationships hr ON hr.hub_id = h.id
            WHERE  hr.rel_qid = @qid
              AND  hr.rel_type IN ('franchise', 'fictional_universe')
              AND  h.parent_hub_id IS NULL
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Hub?>(null);

        var hub = new Hub
        {
            Id             = Guid.Parse(reader.GetString(0)),
            UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
            UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
            ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
        };

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> FindHubIdsByFranchiseQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT hub_id
            FROM   hub_relationships
            WHERE  rel_qid  = @qid
              AND  rel_type IN ('franchise', 'fictional_universe');
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        var results = new List<Guid>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add(Guid.Parse(reader.GetString(0)));

        IReadOnlyList<Guid> result = results;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HubRelationship>> GetRelationshipsAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, hub_id, rel_type, rel_qid, rel_label, confidence, discovered_at
            FROM   hub_relationships
            WHERE  hub_id = @hubId;
            """;
        cmd.Parameters.AddWithValue("@hubId", hubId.ToString());

        var results = new List<HubRelationship>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new HubRelationship
            {
                Id           = Guid.Parse(reader.GetString(0)),
                HubId        = Guid.Parse(reader.GetString(1)),
                RelType      = reader.GetString(2),
                RelQid       = reader.GetString(3),
                RelLabel     = reader.IsDBNull(4) ? null : reader.GetString(4),
                Confidence   = reader.GetDouble(5),
                DiscoveredAt = DateTimeOffset.Parse(reader.GetString(6)),
            });
        }

        IReadOnlyList<HubRelationship> result = results;
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Hub?> GetByIdAsync(Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status, parent_hub_id, wikidata_qid
            FROM   hubs
            WHERE  id = @id
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@id", hubId.ToString());

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Hub?>(null);

        var hub = new Hub
        {
            Id             = Guid.Parse(reader.GetString(0)),
            UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
            UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
            ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
        };

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<Hub?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status,
                   parent_hub_id, wikidata_qid
            FROM   hubs
            WHERE  wikidata_qid = @qid
            LIMIT  1;
            """;
        cmd.Parameters.AddWithValue("@qid", qid);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Task.FromResult<Hub?>(null);

        return Task.FromResult<Hub?>(new Hub
        {
            Id             = Guid.Parse(reader.GetString(0)),
            UniverseId     = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            DisplayName    = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedAt      = DateTimeOffset.Parse(reader.GetString(3)),
            UniverseStatus = reader.IsDBNull(4) ? "Unknown" : reader.GetString(4),
            ParentHubId    = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
            WikidataQid    = reader.IsDBNull(6) ? null : reader.GetString(6),
        });
    }
}
