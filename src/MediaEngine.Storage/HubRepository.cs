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

        var conn  = _db.Open();
        var hubs  = new Dictionary<Guid, Hub>();
        var works = new Dictionary<Guid, Work>();

        // ── Query A: all hubs LEFT JOIN their works ───────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status,
                       w.id, w.media_type, w.sequence_index,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at
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
                    };
                    hubs[hubId] = hub;
                }

                // LEFT JOIN: work columns are NULL when the hub has no works.
                if (!reader.IsDBNull(5))
                {
                    var workId = Guid.Parse(reader.GetString(5));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            HubId              = hubId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(6), ignoreCase: true),
                            SequenceIndex      = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                            UniverseMismatch   = !reader.IsDBNull(8) && reader.GetInt32(8) == 1,
                            UniverseMismatchAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
                            WikidataStatus     = reader.IsDBNull(10) ? "pending" : reader.GetString(10),
                            WikidataCheckedAt  = reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
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
                SELECT entity_id, key, value, last_scored_at
                FROM   canonical_values
                WHERE  entity_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < workIds.Count; i++)
                cmd2.Parameters.AddWithValue($"@p{i}", workIds[i].ToString());

            using var reader2 = cmd2.ExecuteReader();
            while (reader2.Read())
            {
                var entityId = Guid.Parse(reader2.GetString(0));
                if (works.TryGetValue(entityId, out var work))
                {
                    work.CanonicalValues.Add(new CanonicalValue
                    {
                        EntityId     = entityId,
                        Key          = reader2.GetString(1),
                        Value        = reader2.GetString(2),
                        LastScoredAt = DateTimeOffset.Parse(reader2.GetString(3)),
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

        var conn = _db.Open();

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
            SELECT id, universe_id, display_name, created_at, universe_status
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
            var conn = _db.Open();
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
    public async Task AssignWorkToHubAsync(Guid workId, Guid hubId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            var conn = _db.Open();
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
            var conn = _db.Open();
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

        var conn = _db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, universe_id, display_name, created_at, universe_status
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
        };

        return Task.FromResult<Hub?>(hub);
    }

    /// <inheritdoc/>
    public Task<Guid> UpsertAsync(Hub hub, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();

        // INSERT OR IGNORE ensures idempotency for new hubs.
        // UPDATE sets display_name on every call so the latest ingested name wins.
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO hubs(id, universe_id, display_name, created_at, universe_status)
                VALUES (@id, @uid, @dn, @ca, @us);
            UPDATE hubs SET display_name = @dn, universe_status = @us WHERE id = @id;
            """;
        cmd.Parameters.AddWithValue("@id",  hub.Id.ToString());
        cmd.Parameters.AddWithValue("@uid", hub.UniverseId.HasValue
            ? hub.UniverseId.Value.ToString()
            : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@dn",  hub.DisplayName ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ca",  hub.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@us",  hub.UniverseStatus ?? "Unknown");
        cmd.ExecuteNonQuery();

        return Task.FromResult(hub.Id);
    }

    /// <inheritdoc/>
    public Task SetUniverseMismatchAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var conn = _db.Open();
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
}
