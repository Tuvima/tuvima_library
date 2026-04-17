using Dapper;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Storage;

/// <summary>
/// SQLite implementation of <see cref="ICollectionRepository"/>.
/// Loads all collections with their child Works and each Work's CanonicalValues
/// using two sequential queries (no N+1) — same pattern as
/// <see cref="MediaAssetRepository"/>.
///
/// Uses Dapper for simple single-table queries; raw reader retained for the
/// complex multi-table JOIN in <see cref="GetAllAsync"/>.
/// </summary>
public sealed class CollectionRepository : ICollectionRepository
{
    private readonly IDatabaseConnection _db;

    // Reusable SELECT list for single-collection queries (no table prefix needed).
    private const string CollectionSelectColumns = """
        id                AS Id,
        universe_id       AS UniverseId,
        display_name      AS DisplayName,
        created_at        AS CreatedAt,
        universe_status   AS UniverseStatus,
        parent_collection_id     AS ParentCollectionId,
        wikidata_qid      AS WikidataQid,
        collection_type          AS CollectionType,
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

    // Reusable SELECT list for collection_relationships rows.
    private const string RelSelectColumns = """
        id            AS Id,
        collection_id        AS CollectionId,
        rel_type      AS RelType,
        rel_qid       AS RelQid,
        rel_label     AS RelLabel,
        confidence    AS Confidence,
        discovered_at AS DiscoveredAt
        """;

    public CollectionRepository(IDatabaseConnection db)
    {
        ArgumentNullException.ThrowIfNull(db);
        _db = db;
    }

    // -------------------------------------------------------------------------
    // Helpers — post-query fixup for Collection rows
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dapper maps nullable Guid columns to Guid? via the registered type handler,
    /// but UniverseStatus defaults to null when the DB column is NULL.
    /// This helper normalises defaults after Dapper mapping.
    /// </summary>
    private static Collection NormalizeCollection(Collection h)
    {
        h.UniverseStatus ??= "Unknown";
        h.CollectionType ??= "Universe";
        h.Scope ??= "library";
        return h;
    }

    // -------------------------------------------------------------------------
    // GetAllAsync — kept as raw reader (complex 3-query grouping pattern)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<Collection>> GetAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        var collections  = new Dictionary<Guid, Collection>();
        var works = new Dictionary<Guid, Work>();

        // ── Query A: all collections LEFT JOIN their works ───────────────────────────
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_collection_id, h.wikidata_qid,
                       h.collection_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   collections h
                LEFT JOIN works w ON w.collection_id = h.id
                ORDER  BY h.created_at, w.id;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var collectionId = Guid.Parse(reader.GetString(0));
                if (!collections.TryGetValue(collectionId, out var collection))
                {
                    collection = new Collection
                    {
                        Id              = collectionId,
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentCollectionId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        CollectionType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
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
                    collections[collectionId] = collection;
                }

                // LEFT JOIN: work columns are NULL when the collection has no works.
                if (!reader.IsDBNull(19))
                {
                    var workId = Guid.Parse(reader.GetString(19));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            CollectionId              = collectionId,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                            Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                            UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                            UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                            WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                            WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                            WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                        };
                        works[workId] = work;
                        collection.Works.Add(work);
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

        // ── Query C: collection relationships ────────────────────────────────────────
        if (collections.Count > 0)
        {
            var collectionIds     = collections.Keys.ToList();
            var paramNames = collectionIds.Select((_, i) => $"@h{i}").ToList();

            using var cmd3 = conn.CreateCommand();
            cmd3.CommandText = $"""
                SELECT id, collection_id, rel_type, rel_qid, rel_label, confidence, discovered_at
                FROM   collection_relationships
                WHERE  collection_id IN ({string.Join(", ", paramNames)});
                """;

            for (int i = 0; i < collectionIds.Count; i++)
                cmd3.Parameters.AddWithValue($"@h{i}", collectionIds[i].ToString());

            using var reader3 = cmd3.ExecuteReader();
            while (reader3.Read())
            {
                var collectionId = Guid.Parse(reader3.GetString(1));
                if (collections.TryGetValue(collectionId, out var collection))
                {
                    collection.Relationships.Add(new CollectionRelationship
                    {
                        Id           = Guid.Parse(reader3.GetString(0)),
                        CollectionId        = collectionId,
                        RelType      = reader3.GetString(2),
                        RelQid       = reader3.GetString(3),
                        RelLabel     = reader3.IsDBNull(4) ? null : reader3.GetString(4),
                        Confidence   = reader3.GetDouble(5),
                        DiscoveredAt = DateTimeOffset.Parse(reader3.GetString(6)),
                    });
                }
            }
        }

        IReadOnlyList<Collection> result = collections.Values.ToList();
        return Task.FromResult(result);
    }

    // -------------------------------------------------------------------------
    // Single-collection read methods — converted to Dapper
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<Collection?> FindByRelationshipQidAsync(string relType, string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();

        // Find the collection that owns a relationship matching (rel_type, rel_qid).
        var collectionIdStr = conn.ExecuteScalar<string>("""
            SELECT collection_id FROM collection_relationships
            WHERE  rel_type = @relType AND rel_qid = @qid
            LIMIT  1;
            """, new { relType, qid });

        if (collectionIdStr is null)
            return Task.FromResult<Collection?>(null);

        var collectionId = Guid.Parse(collectionIdStr);

        var collection = conn.QueryFirstOrDefault<Collection>($"""
            SELECT {CollectionSelectColumns}
            FROM   collections WHERE id = @id;
            """, new { id = collectionId.ToString() });

        if (collection is null)
            return Task.FromResult<Collection?>(null);

        NormalizeCollection(collection);

        // Load relationships for this collection.
        var rels = conn.Query<CollectionRelationship>($"""
            SELECT {RelSelectColumns}
            FROM   collection_relationships WHERE collection_id = @hid;
            """, new { hid = collectionId.ToString() }).AsList();

        collection.Relationships.AddRange(rels);

        return Task.FromResult<Collection?>(collection);
    }

    /// <inheritdoc/>
    public async Task InsertRelationshipsAsync(IReadOnlyList<CollectionRelationship> relationships, CancellationToken ct = default)
    {
        if (relationships.Count == 0) return;
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            using var tx   = conn.BeginTransaction();

            const string sql = """
                INSERT OR IGNORE INTO collection_relationships (id, collection_id, rel_type, rel_qid, rel_label, confidence, discovered_at)
                VALUES (@Id, @CollectionId, @RelType, @RelQid, @RelLabel, @Confidence, @DiscoveredAt);
                """;

            foreach (var relationship in relationships)
            {
                // Use the entity's CLR property names directly so SQLite receives
                // the exact parameter names Dapper binds for Guid/DateTimeOffset handlers.
                await conn.ExecuteAsync(new CommandDefinition(
                    sql,
                    relationship,
                    transaction: tx,
                    cancellationToken: ct));
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
    public Task<string?> FindCollectionNameByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var result = conn.ExecuteScalar<string>("""
            SELECT h.display_name
            FROM   works w
            JOIN   collections h ON h.id = w.collection_id
            WHERE  w.id = @workId
            LIMIT  1;
            """, new { workId = workId.ToString() });

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public async Task AssignWorkToCollectionAsync(Guid workId, Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE works SET collection_id = @collectionId WHERE id = @workId;",
                new { collectionId = collectionId.ToString(), workId = workId.ToString() });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public async Task MergeCollectionsAsync(Guid keepCollectionId, Guid mergeCollectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            var keep  = keepCollectionId.ToString();
            var merge = mergeCollectionId.ToString();

            using var conn = _db.CreateConnection();
            using var tx   = conn.BeginTransaction();

            // Re-assign all Works from mergeCollection to keepCollection.
            conn.Execute(
                "UPDATE works SET collection_id = @keep WHERE collection_id = @merge;",
                new { keep, merge }, transaction: tx);

            // Move all relationships from mergeCollection to keepCollection (ignore duplicates).
            conn.Execute(
                "UPDATE OR IGNORE collection_relationships SET collection_id = @keep WHERE collection_id = @merge;",
                new { keep, merge }, transaction: tx);

            // Delete any remaining relationships on the merged collection (duplicates that couldn't move).
            conn.Execute(
                "DELETE FROM collection_relationships WHERE collection_id = @merge;",
                new { merge }, transaction: tx);

            // Delete the merged collection.
            conn.Execute(
                "DELETE FROM collections WHERE id = @merge;",
                new { merge }, transaction: tx);

            tx.Commit();
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<Collection?> FindByDisplayNameAsync(string displayName, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var collection = conn.QueryFirstOrDefault<Collection>($"""
            SELECT {CollectionSelectColumns}
            FROM   collections
            WHERE  LOWER(display_name) = LOWER(@name)
            LIMIT  1;
            """, new { name = displayName });

        return Task.FromResult(collection is null ? null : (Collection)NormalizeCollection(collection));
    }

    /// <inheritdoc/>
    public Task<Guid> UpsertAsync(Collection collection, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        conn.Execute("""
            INSERT OR IGNORE INTO collections(id, universe_id, parent_collection_id, display_name, created_at,
                universe_status, wikidata_qid, collection_type, description, icon_name, scope, profile_id,
                is_enabled, is_featured, min_items, rule_json, resolution, rule_hash,
                group_by_field, match_mode, sort_field, sort_direction, live_updating)
                VALUES (@id, @uid, @phid, @dn, @ca, @us, @wqid, @ht, @desc, @icon, @scope, @pid,
                    @enabled, @featured, @minItems, @ruleJson, @resolution, @ruleHash,
                    @groupByField, @matchMode, @sortField, @sortDirection, @liveUpdating);
            UPDATE collections SET display_name = @dn, universe_status = @us, parent_collection_id = @phid,
                            wikidata_qid = @wqid, collection_type = @ht, description = @desc,
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
                id   = collection.Id.ToString(),
                uid  = collection.UniverseId.HasValue ? collection.UniverseId.Value.ToString() : null,
                phid = collection.ParentCollectionId.HasValue ? collection.ParentCollectionId.Value.ToString() : null,
                dn   = collection.DisplayName,
                ca   = collection.CreatedAt.ToString("O"),
                us   = collection.UniverseStatus ?? "Unknown",
                wqid = collection.WikidataQid,
                ht   = collection.CollectionType ?? "Universe",
                desc = collection.Description,
                icon = collection.IconName,
                scope = collection.Scope ?? "library",
                pid  = collection.ProfileId.HasValue ? collection.ProfileId.Value.ToString() : null,
                enabled = collection.IsEnabled ? 1 : 0,
                featured = collection.IsFeatured ? 1 : 0,
                minItems = collection.MinItems,
                ruleJson = collection.RuleJson,
                resolution = collection.Resolution ?? "query",
                ruleHash = collection.RuleHash,
                groupByField = collection.GroupByField,
                matchMode = collection.MatchMode ?? "all",
                sortField = collection.SortField,
                sortDirection = collection.SortDirection ?? "desc",
                liveUpdating = collection.LiveUpdating ? 1 : 0,
            });

        return Task.FromResult(collection.Id);
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

        // Pass 3: Remove collections that have no works (after pass 2),
        // including their child collection_relationships rows.
        conn.Execute("""
            DELETE FROM collection_relationships
            WHERE collection_id NOT IN (
                SELECT DISTINCT collection_id FROM works
            );
            """); // relationships are not counted in the total

        // Only prune ContentGroup / Universe collections — these are the ones whose
        // identity is bound to the works they contain. System view collections (e.g.
        // "Music by Album", "TV by Show"), Smart collections, Mix, Playlist, and Custom
        // collections are query-resolved and intentionally have no work rows; they must
        // never be pruned.
        total += conn.Execute("""
            DELETE FROM collections
            WHERE collection_type IN ('ContentGroup', 'Universe')
              AND id NOT IN (
                SELECT DISTINCT collection_id FROM works WHERE collection_id IS NOT NULL
            );
            """);

        return Task.FromResult(total);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Collection>> GetChildCollectionsAsync(Guid parentCollectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<Collection>($"""
            SELECT {CollectionSelectColumns}
            FROM   collections
            WHERE  parent_collection_id = @parentCollectionId
            ORDER  BY display_name;
            """, new { parentCollectionId = parentCollectionId.ToString() })
            .Select(NormalizeCollection)
            .ToList();

        return Task.FromResult<IReadOnlyList<Collection>>(results);
    }

    /// <inheritdoc/>
    public async Task SetParentCollectionAsync(Guid collectionId, Guid? parentCollectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (parentCollectionId == collectionId)
            return;

        await _db.AcquireWriteLockAsync(ct);
        try
        {
            using var conn = _db.CreateConnection();
            conn.Execute(
                "UPDATE collections SET parent_collection_id = @parentCollectionId WHERE id = @id;",
                new
                {
                    parentCollectionId = parentCollectionId.HasValue ? parentCollectionId.Value.ToString() : null,
                    id          = collectionId.ToString(),
                });
        }
        finally
        {
            _db.ReleaseWriteLock();
        }
    }

    /// <inheritdoc/>
    public Task<Collection?> FindParentCollectionByRelationshipAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var collection = conn.QueryFirstOrDefault<Collection>("""
            SELECT h.id                AS Id,
                   h.universe_id       AS UniverseId,
                   h.display_name      AS DisplayName,
                   h.created_at        AS CreatedAt,
                   h.universe_status   AS UniverseStatus,
                   h.parent_collection_id     AS ParentCollectionId,
                   h.wikidata_qid      AS WikidataQid,
                   h.collection_type          AS CollectionType,
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
            FROM   collections h
            INNER JOIN collection_relationships hr ON hr.collection_id = h.id
            WHERE  hr.rel_qid = @qid
              AND  hr.rel_type IN ('franchise', 'fictional_universe')
              AND  h.parent_collection_id IS NULL
              AND  COALESCE(h.collection_type, 'Universe') <> 'ContentGroup'
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(collection is null ? null : (Collection)NormalizeCollection(collection));
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Guid>> FindCollectionIdsByFranchiseQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<string>("""
            SELECT DISTINCT collection_id
            FROM   collection_relationships
            WHERE  rel_qid  = @qid
              AND  rel_type IN ('franchise', 'fictional_universe');
            """, new { qid })
            .Select(Guid.Parse)
            .ToList();

        return Task.FromResult<IReadOnlyList<Guid>>(results);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<CollectionRelationship>> GetRelationshipsAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var results = conn.Query<CollectionRelationship>($"""
            SELECT {RelSelectColumns}
            FROM   collection_relationships
            WHERE  collection_id = @collectionId;
            """, new { collectionId = collectionId.ToString() }).AsList();

        return Task.FromResult<IReadOnlyList<CollectionRelationship>>(results);
    }

    /// <inheritdoc/>
    public Task<Collection?> GetByIdAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var collection = conn.QueryFirstOrDefault<Collection>($"""
            SELECT {CollectionSelectColumns}
            FROM   collections
            WHERE  id = @id
            LIMIT  1;
            """, new { id = collectionId.ToString() });

        return Task.FromResult(collection is null ? null : (Collection)NormalizeCollection(collection));
    }

    /// <inheritdoc/>
    public Task<Collection?> FindByQidAsync(string qid, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var collection = conn.QueryFirstOrDefault<Collection>($"""
            SELECT {CollectionSelectColumns}
            FROM   collections
            WHERE  wikidata_qid = @qid
            LIMIT  1;
            """, new { qid });

        return Task.FromResult(collection is null ? null : (Collection)NormalizeCollection(collection));
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

    // ── Managed Collection methods ─────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Collection>> GetByTypeAsync(string collectionType, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var collections = (await conn.QueryAsync<Collection>(
            $"SELECT {CollectionSelectColumns} FROM collections WHERE collection_type = @CollectionType ORDER BY display_name",
            new { CollectionType = collectionType })).ToList();
        collections.ForEach(h => NormalizeCollection(h));
        return collections;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Collection>> GetManagedCollectionsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var collections = (await conn.QueryAsync<Collection>(
            $"SELECT {CollectionSelectColumns} FROM collections WHERE collection_type NOT IN ('Universe', 'ContentGroup') ORDER BY collection_type, display_name")).ToList();
        collections.ForEach(h => NormalizeCollection(h));
        return collections;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, int>> GetCountsByTypeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<(string CollectionType, int Count)>(
            "SELECT collection_type AS CollectionType, COUNT(*) AS Count FROM collections WHERE collection_type NOT IN ('Universe', 'ContentGroup') GROUP BY collection_type");
        return rows.ToDictionary(r => r.CollectionType, r => r.Count);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CollectionItem>> GetCollectionItemsAsync(Guid collectionId, int limit = 20, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var items = await conn.QueryAsync<CollectionItem>(
            """
            SELECT id AS Id, collection_id AS CollectionId, work_id AS WorkId,
                   sort_order AS SortOrder, progress_state AS ProgressState,
                   progress_position AS ProgressPosition, added_at AS AddedAt
            FROM collection_items WHERE collection_id = @CollectionId
            ORDER BY sort_order LIMIT @Limit
            """,
            new { CollectionId = collectionId.ToString(), Limit = limit });
        return items.ToList();
    }

    /// <inheritdoc/>
    public async Task<int> GetCollectionItemCountAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM collection_items WHERE collection_id = @CollectionId",
            new { CollectionId = collectionId.ToString() });
    }

    /// <inheritdoc/>
    public async Task UpdateCollectionEnabledAsync(Guid collectionId, bool enabled, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE collections SET is_enabled = @Enabled, modified_at = datetime('now') WHERE id = @Id",
            new { Id = collectionId.ToString(), Enabled = enabled ? 1 : 0 });
    }

    /// <inheritdoc/>
    public async Task UpdateCollectionFeaturedAsync(Guid collectionId, bool featured, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "UPDATE collections SET is_featured = @Featured, modified_at = datetime('now') WHERE id = @Id",
            new { Id = collectionId.ToString(), Featured = featured ? 1 : 0 });
    }

    /// <inheritdoc/>
    public async Task AddCollectionItemAsync(CollectionItem item, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            """
            INSERT INTO collection_items (id, collection_id, work_id, sort_order, progress_state, progress_position, added_at)
            VALUES (@Id, @CollectionId, @WorkId, @SortOrder, @ProgressState, @ProgressPosition, @AddedAt)
            """,
            new
            {
                Id = (item.Id == Guid.Empty ? Guid.NewGuid() : item.Id).ToString(),
                CollectionId = item.CollectionId.ToString(),
                WorkId = item.WorkId.ToString(),
                item.SortOrder,
                item.ProgressState,
                item.ProgressPosition,
                AddedAt = item.AddedAt == default ? DateTimeOffset.UtcNow.ToString("o") : item.AddedAt.ToString("o")
            });
    }

    /// <inheritdoc/>
    public async Task RemoveCollectionItemAsync(Guid itemId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(
            "DELETE FROM collection_items WHERE id = @Id",
            new { Id = itemId.ToString() });
    }

    // -------------------------------------------------------------------------
    // Content Groups — Universe collections that contain works (albums, series, etc.)
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<IReadOnlyList<Collection>> GetContentGroupsAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn  = _db.CreateConnection();
        var collections  = new Dictionary<Guid, Collection>();
        var works = new Dictionary<Guid, Work>();

        // Universe collections that have at least one work assigned.
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_collection_id, h.wikidata_qid,
                       h.collection_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   collections h
                INNER JOIN works w ON w.collection_id = h.id
                WHERE  h.collection_type IN ('ContentGroup', 'Universe')
                ORDER  BY h.display_name, h.created_at, w.ordinal, w.id;
                """;

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var collectionId = Guid.Parse(reader.GetString(0));
                if (!collections.TryGetValue(collectionId, out var collection))
                {
                    collection = new Collection
                    {
                        Id              = collectionId,
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentCollectionId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        CollectionType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
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
                    collections[collectionId] = collection;
                }

                var workId = Guid.Parse(reader.GetString(19));
                if (!works.ContainsKey(workId))
                {
                    var work = new Work
                    {
                        Id                 = workId,
                        CollectionId              = collectionId,
                        MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                        Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                        UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                        UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                        WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                        WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                        WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                    };
                    works[workId] = work;
                    collection.Works.Add(work);
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

        IReadOnlyList<Collection> result = collections.Values.ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<Collection?> GetCollectionWithWorksAsync(Guid collectionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        Collection? collection      = null;
        var  works     = new Dictionary<Guid, Work>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT h.id, h.universe_id, h.display_name, h.created_at,
                       h.universe_status, h.parent_collection_id, h.wikidata_qid,
                       h.collection_type, h.description, h.icon_name, h.scope,
                       h.profile_id, h.is_enabled, h.is_featured, h.min_items,
                       h.rule_json, h.refresh_schedule, h.last_refreshed_at, h.modified_at,
                       w.id, w.media_type, w.ordinal,
                       w.universe_mismatch, w.universe_mismatch_at,
                       w.wikidata_status, w.wikidata_checked_at, w.wikidata_qid
                FROM   collections h
                LEFT JOIN works w ON w.collection_id = h.id
                WHERE  h.id = @CollectionId
                ORDER  BY w.ordinal, w.id;
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@CollectionId";
            p.Value = collectionId.ToString();
            cmd.Parameters.Add(p);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (collection is null)
                {
                    collection = new Collection
                    {
                        Id              = Guid.Parse(reader.GetString(0)),
                        UniverseId      = reader.IsDBNull(1)  ? null : Guid.Parse(reader.GetString(1)),
                        DisplayName     = reader.IsDBNull(2)  ? null : reader.GetString(2),
                        CreatedAt       = DateTimeOffset.Parse(reader.GetString(3)),
                        UniverseStatus  = reader.IsDBNull(4)  ? "Unknown" : reader.GetString(4),
                        ParentCollectionId     = reader.IsDBNull(5)  ? null : Guid.Parse(reader.GetString(5)),
                        WikidataQid     = reader.IsDBNull(6)  ? null : reader.GetString(6),
                        CollectionType         = reader.IsDBNull(7)  ? "Universe" : reader.GetString(7),
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

                // LEFT JOIN: work columns are NULL when collection has no works.
                if (!reader.IsDBNull(19))
                {
                    var workId = Guid.Parse(reader.GetString(19));
                    if (!works.ContainsKey(workId))
                    {
                        var work = new Work
                        {
                            Id                 = workId,
                            CollectionId              = collection.Id,
                            MediaType          = Enum.Parse<MediaType>(reader.GetString(20), ignoreCase: true),
                            Ordinal            = reader.IsDBNull(21) ? null : reader.GetInt32(21),
                            UniverseMismatch   = !reader.IsDBNull(22) && reader.GetInt32(22) == 1,
                            UniverseMismatchAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23)),
                            WikidataStatus     = reader.IsDBNull(24) ? "pending" : reader.GetString(24),
                            WikidataCheckedAt  = reader.IsDBNull(25) ? null : DateTimeOffset.Parse(reader.GetString(25)),
                            WikidataQid        = reader.IsDBNull(26) ? null : reader.GetString(26),
                        };
                        works[workId] = work;
                        collection.Works.Add(work);
                    }
                }
            }
        }

        if (collection is null)
            return Task.FromResult<Collection?>(null);

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

        return Task.FromResult<Collection?>(collection);
    }

    /// <inheritdoc/>
    public Task<Guid?> GetCollectionIdByWorkIdAsync(Guid workId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        using var conn = _db.CreateConnection();
        var raw = conn.QueryFirstOrDefault<string>(
            "SELECT collection_id FROM works WHERE id = @workId AND collection_id IS NOT NULL;",
            new { workId = workId.ToString() });

        if (raw is null) return Task.FromResult<Guid?>(null);
        return Task.FromResult<Guid?>(Guid.Parse(raw));
    }

    /// <inheritdoc/>
    public async Task<Collection?> FindByRuleHashAsync(string ruleHash, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var collection = await conn.QueryFirstOrDefaultAsync<Collection>(
            $"SELECT {CollectionSelectColumns} FROM collections WHERE rule_hash = @Hash LIMIT 1",
            new { Hash = ruleHash });
        return collection is null ? null : NormalizeCollection(collection);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Collection>> GetAllCollectionsForLocationAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var collections = await conn.QueryAsync<Collection>(
            $"SELECT {CollectionSelectColumns} FROM collections WHERE is_enabled = 1 ORDER BY display_name");
        return collections.Select(NormalizeCollection).ToList();
    }
}
