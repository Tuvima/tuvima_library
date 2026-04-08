using MediaEngine.Domain.Models;

namespace MediaEngine.Storage.Tests;

/// <summary>
/// Phase 4 — integration tests for lineage-aware readers. Builds a real
/// SQLite hierarchy (show → season → episode → edition → asset) and verifies
/// that:
///   • RegistryRepository.GetPageAsync reads self-scope fields from the asset
///     row and parent-scope fields from the topmost Work row.
///   • SearchIndexRepository.UpsertByEntityIdAsync self-fetches title from
///     the asset row and author/description from the topmost Work row.
///   • HubRuleEvaluator's CvLookup union finds works whose value lives on
///     either lineage row.
/// </summary>
public sealed class LineageReaderTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseConnection _db;

    public LineageReaderTests()
    {
        DapperConfiguration.Configure();
        _dbPath = Path.Combine(Path.GetTempPath(), $"tuvima_lineage_test_{Guid.NewGuid():N}.db");
        _db = new DatabaseConnection(_dbPath);
        _db.InitializeSchema();
        _db.RunStartupChecks();
    }

    public void Dispose()
    {
        try { _db.Dispose(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }

    // ── RegistryRepository.GetPageAsync ────────────────────────────────────

    [Fact]
    public async Task RegistryGetPage_ReadsTitleFromAsset_AndAuthorFromRootParent()
    {
        // Build a TV hierarchy: show → season → episode → edition → asset
        var (showId, _, episodeId, assetId) = await BuildTvHierarchyAsync();

        // Self-scope: episode title on the asset row.
        await InsertCanonicalAsync(assetId, "title", "Hide and Seek");
        // Parent-scope: author lives on the root show Work row.
        await InsertCanonicalAsync(showId, "author", "Dan Erickson");
        await InsertCanonicalAsync(showId, "genre",  "Sci-Fi");

        var repo = new RegistryRepository(_db);
        var page = await repo.GetPageAsync(new RegistryQuery(IncludeAll: true));

        // The page returns one row per Work in the chain (show + season + episode).
        // The episode is the leaf — it has the asset-row title and inherits the
        // show-row author/genre via the lineage walk.
        var episode = page.Items.Single(i => i.EntityId == episodeId);
        Assert.Equal("Hide and Seek", episode.Title);
        Assert.Equal("Dan Erickson", episode.Author);
        Assert.Equal("Sci-Fi", episode.Genre);
    }

    [Fact]
    public async Task RegistryGetPage_ParentScopeAuthor_NotReadFromAssetRow()
    {
        // Verify that parent-scope fields are NOT silently picked up from the
        // asset row (no fallback). If author is on the asset, it must NOT be
        // returned because the new reader only consults the parent Work row.
        var (_, _, episodeId, assetId) = await BuildTvHierarchyAsync();

        await InsertCanonicalAsync(assetId, "title", "Pilot");
        // Intentionally write author to the WRONG row (asset).
        await InsertCanonicalAsync(assetId, "author", "Wrong Place");
        // Leave the show row empty for author.

        var repo = new RegistryRepository(_db);
        var page = await repo.GetPageAsync(new RegistryQuery(IncludeAll: true));

        var episode = page.Items.Single(i => i.EntityId == episodeId);
        Assert.Equal("Pilot", episode.Title);
        Assert.Null(episode.Author); // No fallback — must stay null.
    }

    [Fact]
    public async Task RegistryGetPage_StandaloneMovie_ReadsParentFieldsFromOwnWork()
    {
        // Standalone movie: parent collapses to self, but parent-scope fields
        // still live on the Work row, NOT the asset row.
        var (workId, _, assetId) = await BuildStandaloneWorkAsync("Movies");

        await InsertCanonicalAsync(assetId, "title", "Dune");
        await InsertCanonicalAsync(workId,  "year",     "2021");
        await InsertCanonicalAsync(workId,  "director", "Denis Villeneuve");

        var repo = new RegistryRepository(_db);
        var page = await repo.GetPageAsync(new RegistryQuery(IncludeAll: true));

        var item = page.Items.Single(i => i.EntityId == workId);
        Assert.Equal("Dune", item.Title);
        Assert.Equal("2021", item.Year);
        Assert.Equal("Denis Villeneuve", item.Director);
    }

    // ── SearchIndexRepository.UpsertByEntityIdAsync ────────────────────────

    [Fact]
    public async Task SearchIndex_Upsert_ReadsTitleFromAssetAndAuthorFromRootParent()
    {
        var (showId, _, _, assetId) = await BuildTvHierarchyAsync();

        await InsertCanonicalAsync(assetId, "title",         "Hide and Seek");
        await InsertCanonicalAsync(assetId, "original_title","Hide and Seek");
        await InsertCanonicalAsync(showId,  "author",        "Dan Erickson");
        await InsertCanonicalAsync(showId,  "description",   "A workplace mystery.");

        var index = new SearchIndexRepository(_db);
        await index.UpsertByEntityIdAsync(assetId);

        // The search row is keyed on the leaf work id (the episode work).
        var (workId, title, author, description) = ReadSearchRowByAssetId(assetId);
        Assert.NotNull(workId);
        Assert.Equal("Hide and Seek", title);
        Assert.Equal("Dan Erickson", author);
        Assert.Equal("A workplace mystery.", description);
    }

    [Fact]
    public async Task SearchIndex_Upsert_AcceptsWorkIdAsEntryPoint()
    {
        // Caller may pass either an asset id or the leaf work id — both
        // resolve to the same FTS row.
        var (showId, _, episodeWorkId, assetId) = await BuildTvHierarchyAsync();

        await InsertCanonicalAsync(assetId, "title",  "Hide and Seek");
        await InsertCanonicalAsync(showId,  "author", "Dan Erickson");

        var index = new SearchIndexRepository(_db);
        await index.UpsertByEntityIdAsync(episodeWorkId);

        var (workId, title, author, _) = ReadSearchRowByAssetId(assetId);
        Assert.Equal(episodeWorkId.ToString(), workId);
        Assert.Equal("Hide and Seek", title);
        Assert.Equal("Dan Erickson", author);
    }

    // ── HubRuleEvaluator (CvLookup union over self+parent) ─────────────────

    [Fact]
    public async Task HubRule_CvLookup_FindsValueOnAssetRow()
    {
        var (_, _, episodeWorkId, assetId) = await BuildTvHierarchyAsync();
        await InsertCanonicalAsync(assetId, "year", "2022");

        // Drop a noise standalone work that should NOT match.
        await BuildStandaloneWorkAsync("Books");

        var evaluator = new HubRuleEvaluator(_db);
        var matches = evaluator.Evaluate(
            [new HubRulePredicate { Field = "year", Op = "eq", Value = "2022" }]);

        // The episode work is found via the asset-row (Self) lookup path.
        Assert.Contains(episodeWorkId, matches);
    }

    [Fact]
    public async Task HubRule_CvLookup_FindsValueOnRootParentRow()
    {
        // Same predicate, but the value lives on the parent show Work id.
        var (showId, _, episodeWorkId, _) = await BuildTvHierarchyAsync();
        await InsertCanonicalAsync(showId, "genre", "Sci-Fi");

        var evaluator = new HubRuleEvaluator(_db);
        var matches = evaluator.Evaluate(
            [new HubRulePredicate { Field = "genre", Op = "eq", Value = "Sci-Fi" }]);

        // The episode work is found via the parent-scope path (Parent lookup
        // walks parent_work_id up two levels and finds the show row).
        Assert.Contains(episodeWorkId, matches);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  Helpers — schema-safe hierarchy builders
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds: hub → show Work → season Work (parent=show) → episode Work
    /// (parent=season) → edition → asset. Returns the IDs as a tuple.
    /// </summary>
    private async Task<(Guid ShowId, Guid SeasonId, Guid EpisodeId, Guid AssetId)>
        BuildTvHierarchyAsync()
    {
        using var conn = _db.CreateConnection();
        var hubId    = Guid.NewGuid();
        var showId   = Guid.NewGuid();
        var seasonId = Guid.NewGuid();
        var epId     = Guid.NewGuid();
        var edId     = Guid.NewGuid();
        var assetId  = Guid.NewGuid();
        var hash     = $"hash_{epId:N}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO hubs (id, created_at) VALUES ('{hubId}', datetime('now'));
            INSERT INTO works (id, hub_id, media_type)
                VALUES ('{showId}', '{hubId}', 'TV');
            INSERT INTO works (id, hub_id, media_type, parent_work_id)
                VALUES ('{seasonId}', '{hubId}', 'TV', '{showId}');
            INSERT INTO works (id, hub_id, media_type, parent_work_id)
                VALUES ('{epId}', '{hubId}', 'TV', '{seasonId}');
            INSERT INTO editions (id, work_id) VALUES ('{edId}', '{epId}');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
                VALUES ('{assetId}', '{edId}', '{hash}', '/lib/test.mkv', 'Normal');
            """;
        await cmd.ExecuteNonQueryAsync();
        return (showId, seasonId, epId, assetId);
    }

    /// <summary>
    /// Builds a flat hierarchy: hub → Work (no parent) → edition → asset.
    /// Returns (workId, editionId, assetId).
    /// </summary>
    private async Task<(Guid WorkId, Guid EditionId, Guid AssetId)>
        BuildStandaloneWorkAsync(string mediaType)
    {
        using var conn = _db.CreateConnection();
        var hubId   = Guid.NewGuid();
        var workId  = Guid.NewGuid();
        var edId    = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var hash    = $"hash_{workId:N}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO hubs (id, created_at) VALUES ('{hubId}', datetime('now'));
            INSERT INTO works (id, hub_id, media_type)
                VALUES ('{workId}', '{hubId}', '{mediaType}');
            INSERT INTO editions (id, work_id) VALUES ('{edId}', '{workId}');
            INSERT INTO media_assets (id, edition_id, content_hash, file_path_root, status)
                VALUES ('{assetId}', '{edId}', '{hash}', '/lib/standalone.bin', 'Normal');
            """;
        await cmd.ExecuteNonQueryAsync();
        return (workId, edId, assetId);
    }

    private async Task InsertCanonicalAsync(Guid entityId, string key, string value)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO canonical_values (entity_id, key, value, last_scored_at)
            VALUES (@entityId, @key, @value, datetime('now'));
            """;
        cmd.Parameters.AddWithValue("@entityId", entityId.ToString());
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@value", value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads the FTS5 search_index row for the leaf work belonging to the
    /// given asset. Returns (workId, title, author, description).
    /// </summary>
    private (string? WorkId, string? Title, string? Author, string? Description)
        ReadSearchRowByAssetId(Guid assetId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT si.entity_id, si.title, si.author, si.description
            FROM search_index si
            JOIN editions e      ON 1=1
            JOIN media_assets ma ON ma.edition_id = e.id
            WHERE ma.id = @assetId
              AND si.entity_id = e.work_id
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("@assetId", assetId.ToString());
        using var rdr = cmd.ExecuteReader();
        if (!rdr.Read()) return (null, null, null, null);
        return (
            rdr.IsDBNull(0) ? null : rdr.GetString(0),
            rdr.IsDBNull(1) ? null : rdr.GetString(1),
            rdr.IsDBNull(2) ? null : rdr.GetString(2),
            rdr.IsDBNull(3) ? null : rdr.GetString(3));
    }
}
