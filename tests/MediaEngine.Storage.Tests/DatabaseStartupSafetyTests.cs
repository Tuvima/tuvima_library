using System.Data;
using System.Threading.Tasks;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage;
using Microsoft.Data.Sqlite;

namespace MediaEngine.Storage.Tests;

public sealed class DatabaseStartupSafetyTests
{
    [Fact]
    public void FreshDatabase_InitializesRequiredTablesIndexesAndWalSettings()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();

        string[] requiredTables =
        [
            "media_assets",
            "editions",
            "works",
            "collections",
            "review_queue",
            "user_states",
            "profiles",
            "ingestion_batches",
            "ingestion_batch_artifacts",
            "search_index",
        ];

        foreach (var table in requiredTables)
        {
            Assert.True(TableExists(conn, table), $"Required table '{table}' was not created.");
        }

        string[] requiredIndexes =
        [
            "idx_review_queue_status",
            "idx_review_queue_entity_id",
            "idx_media_assets_content_hash",
            "idx_media_assets_file_path_root",
            "idx_ingestion_batches_status",
            "idx_ingestion_batch_artifacts_batch",
        ];

        foreach (var index in requiredIndexes)
        {
            Assert.True(IndexExists(conn, index), $"Required index '{index}' was not created.");
        }

        Assert.Equal("wal", Scalar(conn, "PRAGMA journal_mode;").ToLowerInvariant());
        Assert.Equal("1", Scalar(conn, "PRAGMA synchronous;"));
        Assert.Equal("1", Scalar(conn, "PRAGMA foreign_keys;"));
        Assert.Equal("5000", Scalar(conn, "PRAGMA busy_timeout;"));
        Assert.Equal("-16384", Scalar(conn, "PRAGMA cache_size;"));
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void FreshDatabase_UsesGuidBlobStorageEpoch()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("guid-blob-v1", Scalar(conn, "SELECT value FROM storage_metadata WHERE key = 'storage_epoch';"));

        (string Table, string Column)[] internalGuidColumns =
        [
            ("metadata_providers", "id"),
            ("provider_config", "provider_id"),
            ("collections", "id"),
            ("works", "id"),
            ("editions", "id"),
            ("media_assets", "id"),
            ("persons", "id"),
            ("metadata_claims", "id"),
            ("metadata_claims", "entity_id"),
            ("metadata_claims", "provider_id"),
            ("metadata_claims", "decision_source_provider_id"),
            ("ai_feature_artifacts", "entity_id"),
            ("ai_feature_artifacts", "source_provider_id"),
            ("canonical_values", "entity_id"),
            ("canonical_values", "winning_provider_id"),
            ("canonical_value_arrays", "entity_id"),
            ("profiles", "id"),
            ("entity_assets", "id"),
            ("entity_assets", "entity_id"),
            ("pending_person_signals", "id"),
            ("pending_person_signals", "entity_id"),
            ("system_activity", "entity_id"),
            ("system_activity", "profile_id"),
            ("system_activity", "ingestion_run_id"),
            ("review_queue", "id"),
            ("review_queue", "entity_id"),
            ("review_queue", "source_operation_id"),
            ("ingestion_batches", "id"),
            ("ingestion_batch_artifacts", "id"),
            ("ingestion_batch_artifacts", "batch_id"),
            ("ingestion_batch_artifacts", "artifact_id"),
            ("ingestion_batch_artifacts", "parent_entity_id"),
            ("ingestion_log", "id"),
            ("ingestion_log", "media_asset_id"),
            ("ingestion_log", "ingestion_run_id"),
            ("bridge_ids", "id"),
            ("bridge_ids", "entity_id"),
            ("identity_jobs", "id"),
            ("identity_jobs", "entity_id"),
            ("identity_jobs", "ingestion_run_id"),
            ("identity_jobs", "selected_candidate_id"),
            ("retail_match_candidates", "id"),
            ("retail_match_candidates", "job_id"),
            ("wikidata_bridge_candidates", "id"),
            ("wikidata_bridge_candidates", "job_id"),
            ("person_aliases", "pseudonym_person_id"),
            ("person_aliases", "real_person_id"),
            ("person_media_links", "media_asset_id"),
            ("person_media_links", "person_id"),
            ("person_roles", "person_id"),
            ("person_group_members", "group_id"),
            ("person_group_members", "member_id"),
            ("character_performer_links", "person_id"),
            ("character_performer_links", "fictional_entity_id"),
            ("character_portraits", "id"),
            ("character_portraits", "person_id"),
            ("character_portraits", "fictional_entity_id"),
            ("media_operations", "id"),
            ("media_operations", "entity_id"),
            ("media_operations", "batch_id"),
            ("media_operation_events", "id"),
            ("media_operation_events", "operation_id"),
            ("media_operation_events", "entity_id"),
            ("media_operation_events", "batch_id"),
            ("entity_capability_states", "id"),
            ("entity_capability_states", "entity_id"),
            ("entity_capability_states", "last_operation_id"),
            ("entity_events", "id"),
            ("entity_events", "entity_id"),
            ("entity_events", "ingestion_run_id"),
            ("entity_field_changes", "id"),
            ("entity_field_changes", "event_id"),
            ("entity_field_changes", "entity_id"),
            ("alignment_jobs", "id"),
            ("alignment_jobs", "ebook_asset_id"),
            ("alignment_jobs", "audiobook_asset_id"),
            ("api_keys", "id"),
            ("audio_fingerprints", "asset_id"),
            ("collection_items", "id"),
            ("collection_items", "collection_id"),
            ("collection_items", "work_id"),
            ("collection_placements", "id"),
            ("collection_placements", "collection_id"),
            ("collection_relationships", "id"),
            ("collection_relationships", "collection_id"),
            ("collections", "universe_id"),
            ("collections", "parent_collection_id"),
            ("collections", "profile_id"),
            ("deferred_enrichment_queue", "id"),
            ("deferred_enrichment_queue", "entity_id"),
            ("editions", "work_id"),
            ("encode_jobs", "id"),
            ("encode_jobs", "asset_id"),
            ("entity_relationships", "id"),
            ("fictional_entities", "id"),
            ("fictional_entity_work_links", "entity_id"),
            ("plugin_lore_sources", "id"),
            ("plugin_lore_entities", "id"),
            ("plugin_lore_entities", "source_id"),
            ("plugin_lore_relationships", "id"),
            ("plugin_lore_relationships", "source_id"),
            ("media_assets", "edition_id"),
            ("offline_variants", "id"),
            ("offline_variants", "asset_id"),
            ("playback_inspection_cache", "asset_id"),
            ("player_sessions", "profile_id"),
            ("player_sessions", "session_id"),
            ("player_sessions", "current_queue_item_id"),
            ("player_queue_items", "id"),
            ("player_queue_items", "profile_id"),
            ("player_queue_items", "work_id"),
            ("player_queue_items", "asset_id"),
            ("player_queue_items", "collection_id"),
            ("audiobook_listen_active_segments", "profile_id"),
            ("audiobook_listen_active_segments", "work_id"),
            ("audiobook_listen_active_segments", "asset_id"),
            ("audiobook_listen_active_segments", "queue_item_id"),
            ("audiobook_listen_history", "id"),
            ("audiobook_listen_history", "profile_id"),
            ("audiobook_listen_history", "work_id"),
            ("audiobook_listen_history", "asset_id"),
            ("audiobook_bookmarks", "id"),
            ("audiobook_bookmarks", "profile_id"),
            ("audiobook_bookmarks", "work_id"),
            ("audiobook_bookmarks", "asset_id"),
            ("audiobook_chapter_title_overrides", "work_id"),
            ("audiobook_chapter_title_overrides", "asset_id"),
            ("playback_segments", "id"),
            ("playback_segments", "asset_id"),
            ("profile_external_logins", "id"),
            ("profile_external_logins", "profile_id"),
            ("reader_bookmarks", "id"),
            ("reader_bookmarks", "asset_id"),
            ("reader_highlights", "id"),
            ("reader_highlights", "asset_id"),
            ("reader_statistics", "id"),
            ("reader_statistics", "asset_id"),
            ("retail_match_candidates", "provider_id"),
            ("search_results_cache", "entity_id"),
            ("series_manifest_hydrations", "collection_id"),
            ("series_manifest_items", "id"),
            ("series_manifest_items", "collection_id"),
            ("series_manifest_items", "linked_work_id"),
            ("text_tracks", "id"),
            ("text_tracks", "asset_id"),
            ("transaction_log", "entity_id"),
            ("user_playback_settings", "profile_id"),
            ("user_states", "user_id"),
            ("user_states", "asset_id"),
            ("user_taste_profiles", "user_id"),
            ("works", "collection_id"),
            ("works", "parent_work_id"),
        ];

        foreach (var (table, column) in internalGuidColumns)
        {
            Assert.Equal("BLOB", ColumnType(conn, table, column));
        }

        var expectedGuidColumns = internalGuidColumns
            .Select(column => $"{column.Table}.{column.Column}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var actualGuidColumns = DeclaredBlobColumns(conn)
            .Where(value => value != "audio_fingerprints.fingerprint")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedGuidColumns, actualGuidColumns);

        Assert.Equal("TEXT", ColumnType(conn, "works", "wikidata_qid"));
        Assert.Equal("TEXT", ColumnType(conn, "media_assets", "content_hash"));
        Assert.Equal("TEXT", ColumnType(conn, "bridge_ids", "provider_id"));
        Assert.Equal("TEXT", ColumnType(conn, "provider_response_cache", "provider_id"));
    }

    [Fact]
    public async Task CanonicalValueRepository_KeepsMultiValuedKeysOutOfScalarTable()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        var repo = new CanonicalValueRepository(fixture.Database);
        var entityId = Guid.NewGuid();
        await repo.UpsertBatchAsync([
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.Title,
                Value = "Dune",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
            new CanonicalValue
            {
                EntityId = entityId,
                Key = MetadataFieldConstants.Author,
                Value = "Frank Herbert",
                LastScoredAt = DateTimeOffset.UtcNow,
            },
        ]);

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal(1, Convert.ToInt32(Scalar(conn, "SELECT COUNT(*) FROM canonical_values;")));
        Assert.Equal("Dune", Scalar(conn, "SELECT value FROM canonical_values WHERE key = 'title';"));
        Assert.Equal("0", Scalar(conn, "SELECT COUNT(*) FROM canonical_values WHERE key = 'author';"));
    }

    [Fact]
    public async Task CanonicalValueArrayRepository_RejectsScalarAndMalformedEntriesBeforeWriting()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        var repo = new CanonicalValueArrayRepository(fixture.Database);
        var entityId = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => repo.SetValuesAsync(
            entityId,
            MetadataFieldConstants.Title,
            [new CanonicalArrayEntry { Ordinal = 0, Value = "Dune" }]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.SetValuesAsync(
            entityId,
            MetadataFieldConstants.Genre,
            [new CanonicalArrayEntry { Ordinal = -1, Value = "Science fiction" }]));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SetValuesAsync(
            entityId,
            MetadataFieldConstants.Genre,
            [
                new CanonicalArrayEntry { Ordinal = 0, Value = "Science fiction" },
                new CanonicalArrayEntry { Ordinal = 0, Value = "Space opera" },
            ]));
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SetValuesAsync(
            entityId,
            MetadataFieldConstants.Genre,
            [new CanonicalArrayEntry { Ordinal = 0, Value = " " }]));

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("0", Scalar(conn, "SELECT COUNT(*) FROM canonical_value_arrays;"));
    }

    [Fact]
    public void MetadataFieldConstants_ExposeAnImmutableMultiValueCatalog()
    {
        var property = typeof(MetadataFieldConstants).GetProperty(nameof(MetadataFieldConstants.MultiValuedKeys));

        Assert.NotNull(property);
        Assert.Equal(typeof(IReadOnlySet<string>), property.PropertyType);
        Assert.True(MetadataFieldConstants.MultiValuedKeys.Contains("GENRE"));
    }

    [Fact]
    public void StartupInitialization_IsIdempotent()
    {
        using var fixture = TempDatabase.Create();

        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();
        Assert.True(TableExists(conn, "review_queue"));
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void OpenedEmptyDatabase_InitializesAsCurrentSchema()
    {
        using var fixture = TempDatabase.Create();

        var startup = fixture.Database.Open();
        Assert.Equal(ConnectionState.Open, startup.State);

        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("guid-blob-v1", Scalar(conn, "SELECT value FROM storage_metadata WHERE key = 'storage_epoch';"));
        Assert.True(TableExists(conn, "review_queue"));
    }

    [Fact]
    public void LegacyDatabaseWithoutStorageEpoch_FailsStartupByDefault()
    {
        using var fixture = TempDatabase.Create();
        CreateLegacyTextGuidDatabase(fixture.Path);

        var ex = Assert.Throws<InvalidOperationException>(() => fixture.Database.InitializeSchema());

        Assert.Contains("Legacy databases are not migrated in place", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TUVIMA_STORAGE_RESET", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyDatabaseWithoutStorageEpoch_IsBackedUpAndRecreatedWhenResetIsExplicit()
    {
        using var fixture = TempDatabase.Create();
        CreateLegacyTextGuidDatabase(fixture.Path);

        var previous = Environment.GetEnvironmentVariable("TUVIMA_STORAGE_RESET");
        try
        {
            Environment.SetEnvironmentVariable("TUVIMA_STORAGE_RESET", "1");

            fixture.Database.InitializeSchema();
            fixture.Database.RunStartupChecks();
        }
        finally
        {
            Environment.SetEnvironmentVariable("TUVIMA_STORAGE_RESET", previous);
        }

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("guid-blob-v1", Scalar(conn, "SELECT value FROM storage_metadata WHERE key = 'storage_epoch';"));
        Assert.Equal("BLOB", ColumnType(conn, "metadata_providers", "id"));
        Assert.True(TableExists(conn, "review_queue"));

        var backupPattern = $"{Path.GetFileName(fixture.Path)}.legacy-text-guid.*.bak";
        var backupDir = Path.GetDirectoryName(fixture.Path)!;
        Assert.NotEmpty(Directory.GetFiles(backupDir, backupPattern));
    }

    [Fact]
    public void SchemaResource_DoesNotContainRetiredLegacyStorageTokens()
    {
        var root = FindRepoRoot();
        var schema = File.ReadAllText(Path.Combine(root, "src/MediaEngine.Storage/Schema/schema.sql"));

        Assert.DoesNotContain("provider_" + "reg" + "istry", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hub_" + "items", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE IF NOT EXISTS " + "hubs", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("idx_" + "hubs", schema, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("packed delimiter", schema, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortLivedConnections_DoNotDisposeSharedStartupConnection()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        var startup = fixture.Database.Open();
        using (var transient = fixture.Database.CreateConnection())
        {
            Assert.NotSame(startup, transient);
            Assert.Equal(System.Data.ConnectionState.Open, transient.State);
        }

        Assert.Equal(System.Data.ConnectionState.Open, startup.State);
        using var cmd = startup.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master;";
        Assert.True(Convert.ToInt32(cmd.ExecuteScalar()) > 0);
    }

    [Fact]
    public void Open_ReusesSharedStartupConnection()
    {
        using var fixture = TempDatabase.Create();

        var first = fixture.Database.Open();
        var second = fixture.Database.Open();

        Assert.Same(first, second);
        Assert.Equal(ConnectionState.Open, first.State);
    }

    [Fact]
    public void Dispose_ClosesSharedStartupConnection()
    {
        var fixture = TempDatabase.Create();
        var startup = fixture.Database.Open();

        fixture.Dispose();

        Assert.Equal(ConnectionState.Closed, startup.State);
    }

    [Fact]
    public void Vacuum_RemainsCallableAfterStartup()
    {
        using var fixture = TempDatabase.Create();
        fixture.Database.InitializeSchema();
        fixture.Database.RunStartupChecks();

        fixture.Database.Vacuum();

        using var conn = fixture.Database.CreateConnection();
        Assert.Equal("ok", Scalar(conn, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task WriteLock_SerializesAndCanBeReacquired()
    {
        using var fixture = TempDatabase.Create();

        await fixture.Database.AcquireWriteLockAsync();
        var waiterStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiterAcquired = false;

        var waiter = Task.Run(async () =>
        {
            waiterStarted.SetResult();
            await fixture.Database.AcquireWriteLockAsync();
            waiterAcquired = true;
            fixture.Database.ReleaseWriteLock();
        });

        await waiterStarted.Task;
        await Task.Delay(50);
        Assert.False(waiterAcquired);

        fixture.Database.ReleaseWriteLock();
        await waiter;

        await fixture.Database.AcquireWriteLockAsync();
        fixture.Database.ReleaseWriteLock();
    }

    private static bool TableExists(SqliteConnection conn, string name) =>
        Exists(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;", name);

    private static bool IndexExists(SqliteConnection conn, string name) =>
        Exists(conn, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = $name;", name);

    private static bool Exists(SqliteConnection conn, string sql, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static string Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private static string ColumnType(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}]);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return reader.GetString(2);
        }

        throw new InvalidOperationException($"Column {table}.{column} was not found.");
    }

    private static IReadOnlyList<string> DeclaredBlobColumns(SqliteConnection conn)
    {
        var tables = new List<string>();
        using (var tableCmd = conn.CreateCommand())
        {
            tableCmd.CommandText = """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                  AND name NOT LIKE 'search_index_%'
                ORDER BY name;
                """;
            using var tableReader = tableCmd.ExecuteReader();
            while (tableReader.Read())
                tables.Add(tableReader.GetString(0));
        }

        var result = new List<string>();
        foreach (var table in tables)
        {
            using var columnCmd = conn.CreateCommand();
            columnCmd.CommandText = $"PRAGMA table_info([{table}]);";
            using var columnReader = columnCmd.ExecuteReader();
            while (columnReader.Read())
            {
                if (string.Equals(columnReader.GetString(2), "BLOB", StringComparison.OrdinalIgnoreCase))
                    result.Add($"{table}.{columnReader.GetString(1)}");
            }
        }

        return result;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediaEngine.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private static void CreateLegacyTextGuidDatabase(string path)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE metadata_providers (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                version TEXT NOT NULL
            );
            INSERT INTO metadata_providers (id, name, version)
            VALUES ('00000000-0000-0000-0000-000000000001', 'legacy', '1.0');
            """;
        cmd.ExecuteNonQuery();
    }

    private sealed class TempDatabase : IDisposable
    {
        private TempDatabase(string path)
        {
            Path = path;
            Database = new DatabaseConnection(path);
        }

        public string Path { get; }

        public DatabaseConnection Database { get; }

        public static TempDatabase Create() =>
            new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tuvima_startup_{Guid.NewGuid():N}.db"));

        public void Dispose()
        {
            Database.Dispose();
            DatabaseStartupSafetyTests.TryDelete(Path);
            DatabaseStartupSafetyTests.TryDelete($"{Path}-wal");
            DatabaseStartupSafetyTests.TryDelete($"{Path}-shm");
            foreach (var backup in Directory.GetFiles(
                         System.IO.Path.GetDirectoryName(Path)!,
                         $"{System.IO.Path.GetFileName(Path)}.legacy-text-guid.*.bak"))
            {
                DatabaseStartupSafetyTests.TryDelete(backup);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Test cleanup is best effort; failure should not hide the test assertion result.
        }
    }
}
