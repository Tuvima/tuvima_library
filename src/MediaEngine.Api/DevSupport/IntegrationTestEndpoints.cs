using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Providers.Contracts;
using MediaEngine.Providers.Models;
using MediaEngine.Api.Services;
using MediaEngine.Storage.Contracts;
using Microsoft.Extensions.Options;

namespace MediaEngine.Api.DevSupport;

/// <summary>
/// Integration test endpoint that runs a full ingestion cycle, validates each media type,
/// tests manual search, verifies universe enrichment, and produces an HTML report.
///
///   POST /dev/integration-test  — Full cycle: wipe → seed → ingest → validate → report (HTML)
/// </summary>
public static class IntegrationTestEndpoints
{
    // ── Test case definitions ──────────────────────────────────────────────
    //
    // Expectations are now read at runtime from DevSeedEndpoints.GetAllExpectations()
    // so the seed records themselves are the single source of truth. The previous
    // hardcoded TestExpectation[] arrays drifted out of sync with the seed list and
    // were never actually consulted by the reconciliation pass.

    // ── Test result models ────────────────────────────────────────────────

    private sealed class TestReport
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public TimeSpan TotalDuration { get; set; }
        public string WipeStatus { get; set; } = "";
        public string SeedStatus { get; set; } = "";
        public int TotalFilesSeeded { get; set; }
        public TimeSpan IngestionDuration { get; set; }
        /// <summary>Stage level tested: 1, 12, or 123.</summary>
        public int StagesLevel { get; set; } = 12;
        public List<MediaTypeResult> MediaTypeResults { get; set; } = [];
        public List<ManualSearchResult> ManualSearchResults { get; set; } = [];
        public List<UniverseResult> UniverseResults { get; set; } = [];
        public List<VaultCheckResult> VaultChecks { get; set; } = [];
        public List<StageGatingResult> StageGatingResults { get; set; } = [];
        public List<string> IssuesFound { get; set; } = [];
        public List<string> FixesApplied { get; set; } = [];
        public int TotalItems { get; set; }
        public int TotalIdentified { get; set; }
        public int TotalNeedsReview { get; set; }
        public int TotalFailed { get; set; }
        public bool OverallPass => IssuesFound.Count == 0;
        public HashSet<string> ActiveTypes { get; set; } = [];
        public Dictionary<string, string> SkippedTypes { get; set; } = [];
        public Dictionary<string, bool> ProviderHealth { get; set; } = [];
        public ReconciliationSummary? Reconciliation { get; set; }
        /// <summary>
        /// Structured reconciliation report for JSON consumers (e.g. CI tooling).
        /// Populated alongside <see cref="Reconciliation"/> in Phase 4d.
        /// </summary>
        public ReconciliationReport? ReconciliationReport { get; set; }
    }

    /// <summary>Per-item Vault display validation result.</summary>
    private sealed class VaultCheckResult
    {
        public string Title { get; set; } = "";
        public string MediaType { get; set; } = "";
        public Guid EntityId { get; set; }
        public bool HasCoverArt { get; set; }
        public bool HasTitle { get; set; }
        public bool HasCreator { get; set; }
        public bool HasStatus { get; set; }
        public bool HasRetailMatch { get; set; }
        public bool HasWikidataQid { get; set; }
        public string? Status { get; set; }
        public string? CoverUrl { get; set; }
        public string? Creator { get; set; }
        public string? RetailMatch { get; set; }
        public string? WikidataQid { get; set; }
        public bool Pass => HasTitle && HasCreator && HasStatus && HasRetailMatch;
    }

    /// <summary>Stage gating validation result.</summary>
    private sealed class StageGatingResult
    {
        public string Title { get; set; } = "";
        public string Check { get; set; } = "";
        public bool Pass { get; set; }
        public string? Detail { get; set; }
    }

    // ── Reconciliation models ─────────────────────────────────────────────

    private sealed class ReconciliationItemResult
    {
        public string Title { get; set; } = "";
        public string MediaType { get; set; } = "";
        /// <summary>What the seed fixture declared (Identified or InReview with trigger).</summary>
        public string Expected { get; set; } = "";
        /// <summary>What the pipeline actually produced.</summary>
        public string Actual { get; set; } = "";
        /// <summary>Match / UnexpectedReview / UnexpectedIdentified / WrongTrigger / NotFound</summary>
        public string Classification { get; set; } = "";
        /// <summary>Human-readable explanation from the seed fixture, or a generated note.</summary>
        public string? Reason { get; set; }
    }

    private sealed class ReconciliationSummary
    {
        public int ExpectedTotal { get; set; }
        public int Matched { get; set; }
        public List<ReconciliationItemResult> Mismatches { get; set; } = [];
        public Dictionary<string, int> ByClassification { get; set; } = new()
        {
            ["Match"] = 0,
            ["UnexpectedReview"] = 0,
            ["UnexpectedIdentified"] = 0,
            ["WrongTrigger"] = 0,
            ["NotFound"] = 0,
            ["WrongQid"] = 0,
            ["MissingCoverArt"] = 0,
        };
    }

    private sealed class MediaTypeResult
    {
        public string MediaType { get; set; } = "";
        public int Count { get; set; }
        public int Identified { get; set; }
        public int NeedsReview { get; set; }
        public int Failed { get; set; }
        public List<ItemResult> Items { get; set; } = [];
        public bool Pass => Failed == 0 && Count > 0;
    }

    private sealed class ItemResult
    {
        public string Title { get; set; } = "";
        public string FileName { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Status { get; set; } = "";
        public double Confidence { get; set; }
        public string? RetailMatch { get; set; }
        public string? WikidataQid { get; set; }
        public string? ReviewTrigger { get; set; }
        public string? Author { get; set; }
        public string? Year { get; set; }
    }

    private sealed class ManualSearchResult
    {
        public string Query { get; set; } = "";
        public string ProviderName { get; set; } = "";
        public string MediaType { get; set; } = "";
        public int ResultCount { get; set; }
        public string? TopResultTitle { get; set; }
        public double TopResultConfidence { get; set; }
        public string? Error { get; set; }
        public bool Pass => ResultCount > 0 && Error is null;
    }

    private sealed class UniverseResult
    {
        public string Name { get; set; } = "";
        public string? WikidataQid { get; set; }
        public int WorkCount { get; set; }
        public int SeriesCount { get; set; }
        public bool Found { get; set; }
    }

    // ── Dynamic type selection + provider health ─────────────────────────

    private static readonly string[] AllTestableTypes = ["books", "audiobooks", "movies", "tv", "music", "comics"];

    private static readonly Dictionary<string, string[]> ProviderToTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = ["books", "audiobooks", "music"],
        ["tmdb"]        = ["movies", "tv"],
        ["metron"]      = ["comics"],
    };

    private static readonly Dictionary<string, string> ProviderHealthUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = "https://itunes.apple.com/search?term=test&limit=1",
        ["tmdb"]        = "https://api.themoviedb.org/3/configuration",
        ["metron"]      = "https://metron.cloud/api/issue/?series_name=test&limit=1",
    };

    private static HashSet<string> ParseTypes(HttpContext context)
    {
        if (context.Request.Query.TryGetValue("types", out var typesParam) && !string.IsNullOrWhiteSpace(typesParam))
        {
            return typesParam.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .Where(t => AllTestableTypes.Contains(t))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        return new HashSet<string>(AllTestableTypes, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<string, (bool Healthy, string Reason)>> CheckProviderHealthAsync(ILogger logger)
    {
        var results = new Dictionary<string, (bool, string)>(StringComparer.OrdinalIgnoreCase);
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TuvimaLibrary/1.0 (integration-test)");

        var tasks = ProviderHealthUrls.Select(async kvp =>
        {
            try
            {
                if (kvp.Key.Equals("metron", StringComparison.OrdinalIgnoreCase))
                {
                    var creds = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("Shyatic:fgn4vfg*wqx_MZK@cup"));
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", creds);
                }

                using var response = await httpClient.GetAsync(kvp.Value);
                bool ok = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                return (kvp.Key, Healthy: ok, Reason: ok ? "OK" : $"HTTP {(int)response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (kvp.Key, Healthy: false, Reason: ex.GetType().Name);
            }
        });

        foreach (var result in await Task.WhenAll(tasks))
        {
            results[result.Key] = (result.Healthy, result.Reason);
            logger.LogInformation("[HealthCheck] {Provider}: {Status} ({Reason})", result.Key,
                result.Healthy ? "HEALTHY" : "UNAVAILABLE", result.Reason);
        }

        return results;
    }

    private static (HashSet<string> ActiveTypes, Dictionary<string, string> SkipReasons) ResolveActiveTypes(
        HashSet<string> requestedTypes,
        Dictionary<string, (bool Healthy, string Reason)> health)
    {
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in AllTestableTypes)
        {
            if (!requestedTypes.Contains(type))
            {
                skipped[type] = "Not requested";
                continue;
            }

            var gatingProvider = ProviderToTypes
                .Where(kvp => kvp.Value.Contains(type, StringComparer.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (gatingProvider is null)
            {
                active.Add(type);
                continue;
            }

            if (health.TryGetValue(gatingProvider, out var status) && status.Healthy)
                active.Add(type);
            else
                skipped[type] = $"Provider '{gatingProvider}' unavailable ({(health.TryGetValue(gatingProvider!, out var s) ? s.Reason : "unknown")})";
        }

        return (active, skipped);
    }

    // ── Endpoint registration ─────────────────────────────────────────────

    public static void MapIntegrationTestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dev").WithTags("Development");

        group.MapPost("/integration-test", RunIntegrationTestAsync)
            .WithSummary("Full integration test: wipe → seed → ingest → validate → HTML report")
            .Produces(200, contentType: "text/html");
    }

    // ── POST /dev/integration-test ────────────────────────────────────────

    private static async Task<IResult> RunIntegrationTestAsync(
        HttpContext context,
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        IRegistryRepository registryRepo,
        IReviewQueueRepository reviewRepo,
        IEnumerable<IExternalMetadataProvider> providers,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("IntegrationTest");
        var report = new TestReport();
        var sw = Stopwatch.StartNew();

        // ── Parse optional stages parameter ─────────────────────────────
        // 1 = Stage 1 only (retail), 12 = Stage 1+2 (default), 123 = full pipeline
        int stages = 12;
        if (context.Request.Query.TryGetValue("stages", out var stagesParam) && int.TryParse(stagesParam, out var s))
            stages = s;
        report.StagesLevel = stages;

        string stageLabel = stages switch
        {
            1 => "Stage 1 (Retail Identification only)",
            12 => "Stage 1+2 (Retail + Wikidata)",
            123 => "Stage 1+2+3 (Full pipeline including Universe Enrichment)",
            _ => $"Stage level {stages} (unknown — defaulting to 1+2)"
        };

        // ── Parse optional types parameter ──────────────────────────────
        var requestedTypes = ParseTypes(context);
        var health = await CheckProviderHealthAsync(logger);
        var (activeTypes, skipReasons) = ResolveActiveTypes(requestedTypes, health);
        report.ActiveTypes = activeTypes;
        report.SkippedTypes = skipReasons;
        report.ProviderHealth = health.ToDictionary(h => h.Key, h => h.Value.Healthy);

        string typesLabel = string.Join(", ", activeTypes.OrderBy(t => t));

        logger.LogInformation("╔══════════════════════════════════════════╗");
        logger.LogInformation("║   INTEGRATION TEST — Starting            ║");
        logger.LogInformation("║   {StageLabel}                           ║", stageLabel);
        logger.LogInformation("║   Active types: {Types}                  ║", typesLabel);
        if (skipReasons.Count > 0)
            logger.LogInformation("║   Skipped: {Skipped}                     ║",
                string.Join(", ", skipReasons.Select(s => $"{s.Key} ({s.Value})")));
        logger.LogInformation("╚══════════════════════════════════════════╝");

        // ── Phase 1: Wipe ─────────────────────────────────────────────────
        logger.LogInformation("[Phase 1] Wiping database and watch folders...");
        try
        {
            await WipeInternalAsync(db, options, configLoader, ingestionEngine, logger);
            report.WipeStatus = "OK";
        }
        catch (Exception ex)
        {
            report.WipeStatus = $"FAILED: {ex.Message}";
            report.IssuesFound.Add($"Wipe failed: {ex.Message}");
        }

        // ── Phase 2: Seed ────────────────────────────────────────────────
        logger.LogInformation("[Phase 2] Seeding test files...");
        try
        {
            int seeded = await SeedInternalAsync(options, configLoader, activeTypes, logger);
            report.SeedStatus = $"OK — {seeded} files";
            report.TotalFilesSeeded = seeded;
        }
        catch (Exception ex)
        {
            report.SeedStatus = $"FAILED: {ex.Message}";
            report.IssuesFound.Add($"Seed failed: {ex.Message}");
        }

        // ── Phase 3: Trigger scans and wait for ingestion ─────────────────
        // Brief settle to let file system flush.
        await Task.Delay(2000, ct);

        logger.LogInformation("[Phase 3] Triggering directory scans...");
        var scanConfig = configLoader.LoadLibraries();
        foreach (var lib in scanConfig.Libraries)
        {
            if (!string.IsNullOrWhiteSpace(lib.SourcePath) && Directory.Exists(lib.SourcePath))
            {
                int fileCount = Directory.GetFiles(lib.SourcePath, "*", SearchOption.AllDirectories).Length;
                ingestionEngine.ScanDirectory(lib.SourcePath, lib.IncludeSubdirectories);
                logger.LogInformation("  Scan triggered: {Path} ({Category}, {Files} files)", lib.SourcePath, lib.Category, fileCount);
            }
        }

        logger.LogInformation("[Phase 3] Waiting for ingestion to complete (timeout: 8 minutes)...");
        var ingestionSw = Stopwatch.StartNew();
        bool ingestionComplete = await WaitForIngestionAsync(db, logger, TimeSpan.FromMinutes(8), report.TotalFilesSeeded, ct);
        ingestionSw.Stop();
        report.IngestionDuration = ingestionSw.Elapsed;

        if (!ingestionComplete)
        {
            report.IssuesFound.Add("Ingestion did not complete within 8 minutes");
            logger.LogWarning("[Phase 3] Ingestion timeout — proceeding with partial results");
        }

        // ── Phase 4: Validate results per media type ──────────────────────
        logger.LogInformation("[Phase 4] Validating ingestion results...");
        await ValidateResultsAsync(registryRepo, report, logger, ct);

        // ── Phase 4b: Vault Display Validation ──────────────────────────
        logger.LogInformation("[Phase 4b] Vault display validation...");
        await ValidateVaultDisplayAsync(registryRepo, report, stages, logger, ct);

        // ── Phase 4c: Stage Gating Validation ───────────────────────────
        logger.LogInformation("[Phase 4c] Stage gating validation...");
        await ValidateStageGatingAsync(registryRepo, report, stages, logger, ct);

        // ── Phase 4d: Reconciliation — expected vs. actual outcomes ─────────
        logger.LogInformation("[Phase 4d] Running reconciliation pass...");
        await RunReconciliationAsync(db, report, activeTypes, logger, ct);

        // ── Phase 5: Test manual search for review items ──────────────────
        logger.LogInformation("[Phase 5] Testing manual search on review items...");
        await TestManualSearchAsync(registryRepo, providers, report, logger, ct);

        // ── Phase 6: Check universe enrichment ────────────────────────────
        logger.LogInformation("[Phase 6] Checking universe enrichment...");
        await CheckUniversesAsync(registryRepo, report, logger, ct);

        // ── Phase 7: Stage 3 — Universe Enrichment (conditional) ────────
        if (stages >= 123)
        {
            logger.LogInformation("[Phase 7] Triggering Stage 3 Universe Enrichment...");
            await RunStage3EnrichmentAsync(context, registryRepo, db, report, logger, ct);
        }

        sw.Stop();
        report.TotalDuration = sw.Elapsed;

        // ── Generate HTML report ──────────────────────────────────────────
        string html = GenerateHtmlReport(report);

        // Save to disk — prefer repo root tools/reports/, fall back to CWD
        string reportsDir = Path.Combine(
            Path.GetDirectoryName(typeof(IntegrationTestEndpoints).Assembly.Location) ?? ".",
            "..", "..", "..", "..", "..", "tools", "reports");
        string? savedAbsolutePath = null;
        try
        {
            if (!Directory.Exists(reportsDir))
                reportsDir = Path.Combine(Directory.GetCurrentDirectory(), "tools", "reports");
            if (!Directory.Exists(reportsDir))
                Directory.CreateDirectory(reportsDir);

            string fileName = $"integration-test-{DateTime.Now:yyyy-MM-dd-HHmmss}.html";
            string filePath = Path.Combine(reportsDir, fileName);
            await File.WriteAllTextAsync(filePath, html, ct);
            savedAbsolutePath = Path.GetFullPath(filePath);
            logger.LogInformation("[TEST] HTML report saved to: {Path}", savedAbsolutePath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[Report] Could not save report to disk");
        }

        logger.LogInformation("╔══════════════════════════════════════════╗");
        logger.LogInformation("║   INTEGRATION TEST — Complete            ║");
        logger.LogInformation("║   Duration: {Duration}                   ║", report.TotalDuration);
        logger.LogInformation("║   Result: {Result}                       ║", report.OverallPass ? "PASS" : "ISSUES FOUND");
        logger.LogInformation("╚══════════════════════════════════════════╝");

        return Results.Content(html, "text/html");
    }

    // ── Internal wipe ─────────────────────────────────────────────────────

    private static async Task WipeInternalAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        ILogger logger)
    {
        // NOTE: We do NOT call ingestionEngine.StopAsync/Start here.
        // StopAsync permanently closes the debounce channel, and Start() does not
        // reinitialize it — causing all subsequent file processing to silently fail.
        // Instead, we just wipe the data while the engine keeps running. The channel
        // stays alive, so files scanned after the wipe are processed normally.

        // Wipe library root
        string? libraryRoot = options.Value.LibraryRoot;
        if (!string.IsNullOrWhiteSpace(libraryRoot) && Directory.Exists(libraryRoot))
            WipeDirectoryContents(libraryRoot);

        // Wipe each library source path (typed folders first, then General).
        // Skip the General/root watch folder — wiping it destroys the typed subfolders.
        var libConfig = configLoader.LoadLibraries();
        foreach (var lib in libConfig.Libraries)
        {
            if (!string.IsNullOrWhiteSpace(lib.SourcePath) && Directory.Exists(lib.SourcePath))
            {
                // Only wipe files, not subdirectories, for the General (root) folder
                // to avoid destroying the typed watch subfolders.
                bool isGeneral = lib.Category.Equals("General", StringComparison.OrdinalIgnoreCase);
                if (isGeneral)
                    WipeFilesOnly(lib.SourcePath);
                else
                    WipeDirectoryContents(lib.SourcePath);
                logger.LogInformation("[Wipe] Cleared {Category} source: {Path}", lib.Category, lib.SourcePath);
            }
        }

        // Wipe database — DELETE rows instead of dropping tables to avoid breaking
        // any active engine queries or the debounce channel. Schema stays intact.
        // IMPORTANT: foreign_keys pragma must be set outside a transaction and on
        // the SAME connection that will execute the DELETEs, because it is per-connection.
        await db.AcquireWriteLockAsync();
        try
        {
            var conn = db.Open();

            // Disable FK checks for this connection — allows any DELETE order.
            using (var fkOff = conn.CreateCommand()) { fkOff.CommandText = "PRAGMA foreign_keys = OFF;"; fkOff.ExecuteNonQuery(); }

            var tables = new List<string>();
            using (var listCmd = conn.CreateCommand())
            {
                // Preserve system/config tables that the running engine needs for FK references.
                listCmd.CommandText = """
                    SELECT name FROM sqlite_master
                    WHERE type='table'
                      AND name NOT LIKE 'sqlite_%'
                      AND name NOT IN ('schema_version','provider_registry','provider_config','profiles','api_keys')
                    """;
                using var reader = listCmd.ExecuteReader();
                while (reader.Read()) tables.Add(reader.GetString(0));
            }

            int deleted = 0;
            foreach (string table in tables)
            {
                using var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = $"DELETE FROM [{table}];";
                try { deleted += deleteCmd.ExecuteNonQuery(); }
                catch (Exception ex) { logger.LogWarning("[Wipe] DELETE FROM [{Table}] failed: {Msg}", table, ex.Message); }
            }

            // Rebuild FTS5 virtual tables — DELETE FROM leaves them in a corrupt state.
            try
            {
                using var rebuildCmd = conn.CreateCommand();
                rebuildCmd.CommandText = "INSERT INTO search_index(search_index) VALUES('rebuild');";
                rebuildCmd.ExecuteNonQuery();
            }
            catch (Exception ex) { logger.LogWarning("[Wipe] FTS5 rebuild failed: {Msg}", ex.Message); }

            // Re-enable FK checks.
            using (var fkOn = conn.CreateCommand()) { fkOn.CommandText = "PRAGMA foreign_keys = ON;"; fkOn.ExecuteNonQuery(); }

            logger.LogInformation("[Wipe] Database wiped: {Deleted} rows deleted from {Tables} tables, schema preserved", deleted, tables.Count);
        }
        finally { db.ReleaseWriteLock(); }
    }

    // ── Internal seed ────────────────────────────────────────────────────

    private static async Task<int> SeedInternalAsync(
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        HashSet<string> activeTypes,
        ILogger logger)
    {
        var libConfig = configLoader.LoadLibraries();
        int total = 0;
        int failed = 0;

        async Task TryWriteAsync(string filePath, string title, Func<byte[]> build)
        {
            try
            {
                byte[] bytes = build();
                await File.WriteAllBytesAsync(filePath, bytes);
                total++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogWarning(ex, "[Seed] Failed to create '{Title}' at {Path}", title, filePath);
            }
        }

        string? ResolveDir(string category)
        {
            var lib = libConfig.Libraries.FirstOrDefault(l =>
                l.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
            if (lib is not null && !string.IsNullOrWhiteSpace(lib.SourcePath))
                return lib.SourcePath;
            return libConfig.Libraries.Count > 0 ? libConfig.Libraries[0].SourcePath : options.Value.WatchDirectory;
        }

        void EnsureDir(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && !Directory.Exists(path))
                Directory.CreateDirectory(path);
        }

        // Log resolved directories for debugging.
        logger.LogInformation("[Seed] Resolved directories: Books={Books}, Movies={Movies}, TV={TV}, Music={Music}",
            ResolveDir("Books"), ResolveDir("Movies"), ResolveDir("TV"), ResolveDir("Music"));

        // Books
        var booksDir = ResolveDir("Books");
        if (!string.IsNullOrWhiteSpace(booksDir))
        {
            EnsureDir(booksDir);
            if (activeTypes.Contains("books"))
            {
                foreach (var book in DevSeedEndpoints_SeedBooks())
                {
                    string fileName = $"{SanitizeFileName(book.Title)}.epub";
                    string filePath = Path.Combine(booksDir, fileName);
                    if (File.Exists(filePath)) continue;
                    await TryWriteAsync(filePath, book.Title, () =>
                        EpubBuilder.Create(book.Title, book.Author, book.Isbn, book.Year, book.Description,
                            book.Publisher, book.Language, book.AdditionalAuthors, book.Series, book.SeriesPosition));
                }
            }

            if (activeTypes.Contains("audiobooks"))
            {
                // Audiobooks share the Books library folder so the folder prior
                // (config/libraries.json media_types: ["Books", "Audiobooks"]) applies.
                // Using the same booksDir ensures the IngestionEngine's folder-matching
                // StartsWith check finds the configured folder and boosts audiobook
                // classification confidence.
                var audiobooksDir = booksDir;
                foreach (var ab in DevSeedEndpoints_SeedAudiobooks())
                {
                    string fileName = $"{SanitizeFileName(ab.Title)} - {SanitizeFileName(ab.Narrator)}.mp3";
                    string filePath = Path.Combine(audiobooksDir, fileName);
                    if (File.Exists(filePath)) continue;
                    await TryWriteAsync(filePath, ab.Title, () =>
                        Mp3Builder.Create(ab.Title, ab.Artist, narrator: ab.Narrator,
                            year: ab.Year, language: ab.Language, series: ab.Series, seriesPosition: ab.SeriesPosition, asin: ab.Asin));
                }
            }
        }

        // Movies
        var moviesDir = ResolveDir("Movies");
        if (activeTypes.Contains("movies") && !string.IsNullOrWhiteSpace(moviesDir))
        {
            EnsureDir(moviesDir);
            foreach (var v in DevSeedEndpoints_SeedVideos().Where(v => v.MediaType == "Movie"))
            {
                string fileName = $"{SanitizeFileName(v.Title)} ({v.Year}).mp4";
                string filePath = Path.Combine(moviesDir, fileName);
                if (File.Exists(filePath)) continue;
                await TryWriteAsync(filePath, v.Title, () => Mp4Builder.Create(v.Title, v.Director, v.Year));
            }
        }

        // TV
        var tvDir = ResolveDir("TV");
        if (activeTypes.Contains("tv") && !string.IsNullOrWhiteSpace(tvDir))
        {
            EnsureDir(tvDir);
            foreach (var v in DevSeedEndpoints_SeedVideos().Where(v => v.MediaType == "TV"))
            {
                string fileName = v.SeasonNumber is not null && v.EpisodeNumber is not null
                    ? $"{SanitizeFileName(v.Series ?? v.Title)} S{v.SeasonNumber:D2}E{v.EpisodeNumber:D2}.mp4"
                    : $"{SanitizeFileName(v.Title)} ({v.Year}).mp4";
                string filePath = Path.Combine(tvDir, fileName);
                if (File.Exists(filePath)) continue;
                await TryWriteAsync(filePath, v.Title, () => Mp4Builder.Create(
                    v.Title, v.Director, v.Year,
                    showName: v.Series,
                    seasonNumber: v.SeasonNumber,
                    episodeNumber: v.EpisodeNumber));
            }
        }

        // Music
        var musicDir = ResolveDir("Music");
        if (activeTypes.Contains("music") && !string.IsNullOrWhiteSpace(musicDir))
        {
            EnsureDir(musicDir);
            foreach (var m in DevSeedEndpoints_SeedMusic())
            {
                string fileName = $"{SanitizeFileName(m.Artist)} - {SanitizeFileName(m.Title)}.flac";
                string filePath = Path.Combine(musicDir, fileName);
                if (File.Exists(filePath)) continue;
                await TryWriteAsync(filePath, m.Title, () =>
                    FlacBuilder.Create(m.Title, m.Artist, m.Album, m.Year, m.Genre, m.TrackNumber));
            }
        }

        // Comics
        var comicsDir = ResolveDir("Comics");
        if (activeTypes.Contains("comics") && !string.IsNullOrWhiteSpace(comicsDir))
        {
            EnsureDir(comicsDir);
            foreach (var c in DevSeedEndpoints_SeedComics())
            {
                string fileName = $"{SanitizeFileName(c.Title)}.cbz";
                string filePath = Path.Combine(comicsDir, fileName);
                if (File.Exists(filePath)) continue;
                await TryWriteAsync(filePath, c.Title, () =>
                    CbzBuilder.Create(c.Title, c.Writer, c.Series, c.Number,
                        c.Year, c.Genre, c.Summary, c.Publisher, c.Penciller));
            }
        }

        logger.LogInformation("[Seed] {Count} test files created ({Failed} failed)", total, failed);

        // Allow file handles to fully release before the filesystem watcher triggers.
        // Without this, the DebounceQueue's file-lock probe may fail repeatedly on
        // MP3 and EPUB files that are still held by the OS write cache.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(3000);

        return total;
    }

    // ── Wait for ingestion ────────────────────────────────────────────────

    private static async Task<bool> WaitForIngestionAsync(
        IDatabaseConnection db,
        ILogger logger,
        TimeSpan timeout,
        int expectedCount,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        // Phase 1: Wait for asset ingestion to reach the expected count.
        // Use two criteria: reach the expected file count OR stabilize at 8+ consecutive
        // polls (24+ seconds). Different media types arrive at different rates — books
        // take much longer than video due to file-lock probing and EPUB processing.
        int lastAssetCount = 0;
        int assetStable = 0;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(3000, ct);
            int assetCount;
            using (var conn = db.CreateConnection())
                assetCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM media_assets;");

            if (assetCount == lastAssetCount && assetCount > 0) assetStable++;
            else assetStable = 0;
            lastAssetCount = assetCount;

            logger.LogInformation("  Ingestion: {Count}/{Expected} assets, stable={Stable}/8", assetCount, expectedCount, assetStable);
            // Exit when we have all expected assets OR have been stable for 24+ seconds
            if (assetCount >= expectedCount) break;
            if (assetCount > 0 && assetStable >= 8) break;
        }

        // Phase 2: Wait for hydration (review queue + QID resolution) to complete.
        // Track three metrics: total works, "resolved" works (QID/review/curator_state),
        // and metadata_claims count. Hydration is done when ALL works are resolved and
        // claims have stopped growing. The key insight: items still in Registered or
        // AwaitingStage2 state haven't finished hydration — we must wait for them too.
        int lastVisibleCount = 0;
        int lastClaimCount = 0;
        int hydrationStable = 0;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct);
            int totalWorks, visibleCount, claimCount, pendingCount;
            using (var conn = db.CreateConnection())
            {
                totalWorks = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM works;");
                visibleCount = conn.ExecuteScalar<int>("""
                    SELECT COUNT(DISTINCT w.id) FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN review_queue rq ON rq.entity_id = ma.id AND rq.status = 'Pending'
                    WHERE w.wikidata_qid IS NOT NULL OR rq.id IS NOT NULL OR w.curator_state IS NOT NULL
                    """);
                claimCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM metadata_claims;");
                // Count works that haven't finished hydration: no QID, no review entry, no curator_state.
                // These are still in Registered or AwaitingStage2 — hydration hasn't completed for them.
                pendingCount = conn.ExecuteScalar<int>("""
                    SELECT COUNT(DISTINCT w.id) FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN review_queue rq ON rq.entity_id = ma.id AND rq.status = 'Pending'
                    WHERE w.wikidata_qid IS NULL AND rq.id IS NULL AND w.curator_state IS NULL
                    """);
            }

            bool countsStable = visibleCount == lastVisibleCount && claimCount == lastClaimCount;
            if (countsStable && visibleCount > 0) hydrationStable++;
            else hydrationStable = 0;
            lastVisibleCount = visibleCount;
            lastClaimCount = claimCount;

            logger.LogInformation("  Hydration: {Visible}/{Total} works resolved, {Pending} pending, {Claims} claims, stable={Stable}/8",
                visibleCount, totalWorks, pendingCount, claimCount, hydrationStable);
            // All works resolved (none pending) and stable
            if (visibleCount >= totalWorks && totalWorks > 0 && pendingCount == 0 && hydrationStable >= 3) return true;
            // No pending items and claims stable for extended period
            if (pendingCount == 0 && visibleCount > 0 && hydrationStable >= 5) return true;
            // Fallback: claims completely stable for very long period (some items may never resolve)
            if (visibleCount > 0 && hydrationStable >= 12) return true;
        }

        return lastVisibleCount > 0;
    }

    // ── Validate results ──────────────────────────────────────────────────

    private static async Task ValidateResultsAsync(
        IRegistryRepository registryRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Get all items
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        report.TotalItems = allItems.TotalCount;

        logger.LogInformation("  Total items in registry: {Count}", allItems.TotalCount);

        // Group by media type
        var grouped = allItems.Items.GroupBy(i => i.MediaType).OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            var mtResult = new MediaTypeResult { MediaType = group.Key, Count = group.Count() };

            foreach (var item in group)
            {
                var ir = new ItemResult
                {
                    Title = item.Title,
                    FileName = item.FileName ?? "",
                    MediaType = item.MediaType,
                    Status = item.Status ?? "",
                    Confidence = item.Confidence,
                    RetailMatch = item.RetailMatch,
                    WikidataQid = item.WikidataQid,
                    ReviewTrigger = item.ReviewTrigger,
                    Author = item.Author,
                    Year = item.Year,
                };
                mtResult.Items.Add(ir);

                var statusUpper = (item.Status ?? "").ToUpperInvariant();
                if (statusUpper.Contains("IDENTIFIED") || statusUpper.Contains("CONFIRMED") || statusUpper.Contains("REGISTERED"))
                    mtResult.Identified++;
                else if (statusUpper.Contains("REVIEW"))
                    mtResult.NeedsReview++;
                else if (statusUpper.Contains("FAIL") || statusUpper.Contains("QUARANTINE"))
                    mtResult.Failed++;
                else
                    mtResult.NeedsReview++; // Default to review if unknown status
            }

            report.MediaTypeResults.Add(mtResult);
            report.TotalIdentified += mtResult.Identified;
            report.TotalNeedsReview += mtResult.NeedsReview;
            report.TotalFailed += mtResult.Failed;

            logger.LogInformation("  {Type}: {Count} items ({Identified} identified, {Review} review, {Failed} failed)",
                group.Key, mtResult.Count, mtResult.Identified, mtResult.NeedsReview, mtResult.Failed);
        }

        // Check for expected media types that have zero items
        foreach (var expected in report.ActiveTypes.Select(t => t switch
        {
            "books" => "Books", "audiobooks" => "Audiobooks", "movies" => "Movies",
            "tv" => "TV", "music" => "Music", "comics" => "Comics", _ => t
        }))
        {
            if (!report.MediaTypeResults.Any(r => r.MediaType.Equals(expected, StringComparison.OrdinalIgnoreCase)))
            {
                report.IssuesFound.Add($"No items found for media type: {expected}");
                report.MediaTypeResults.Add(new MediaTypeResult { MediaType = expected, Count = 0 });
            }
        }
    }

    // ── Test manual search ────────────────────────────────────────────────

    private static async Task TestManualSearchAsync(
        IRegistryRepository registryRepo,
        IEnumerable<IExternalMetadataProvider> providers,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Test one search per media type using the correct provider
        var allSearchTests = new (string query, string providerName, string mediaType, MediaType enumType, string typeKey)[]
        {
            ("Dune Frank Herbert", "apple_api", "Books", MediaType.Books, "books"),
            ("Blade Runner 2049", "tmdb", "Movies", MediaType.Movies, "movies"),
            ("Breaking Bad", "tmdb", "TV", MediaType.TV, "tv"),
            ("Bohemian Rhapsody Queen", "apple_api", "Music", MediaType.Music, "music"),
            ("Lose Yourself Eminem", "apple_api", "Music", MediaType.Music, "music"),
            ("Yesterday Beatles", "apple_api", "Music", MediaType.Music, "music"),
            ("Clair de Lune Debussy", "apple_api", "Music", MediaType.Music, "music"),
            ("Batman Year One", "metron", "Comics", MediaType.Comics, "comics"),
        };
        var searchTests = allSearchTests
            .Where(t => report.ActiveTypes.Contains(t.typeKey))
            .Select(t => (t.query, t.providerName, t.mediaType, t.enumType))
            .ToArray();

        var providerList = providers.ToList();

        foreach (var (query, providerName, mediaType, enumType) in searchTests)
        {
            var result = new ManualSearchResult
            {
                Query = query,
                ProviderName = providerName,
                MediaType = mediaType,
            };

            try
            {
                // Find the provider
                var provider = providerList.FirstOrDefault(p =>
                    p.Name.Equals(providerName, StringComparison.OrdinalIgnoreCase));

                if (provider is null)
                {
                    result.Error = $"Provider '{providerName}' not found in registered providers";
                    report.IssuesFound.Add($"Manual search: provider '{providerName}' not found for {mediaType}");
                }
                else
                {
                    var lookupRequest = new ProviderLookupRequest
                    {
                        Title = query,
                        MediaType = enumType,
                    };

                    var searchResults = await provider.SearchAsync(lookupRequest, 5, ct);
                    result.ResultCount = searchResults.Count;

                    if (searchResults.Count > 0)
                    {
                        result.TopResultTitle = searchResults[0].Title;
                        result.TopResultConfidence = searchResults[0].Confidence;
                        logger.LogInformation("  Search '{Query}' via {Provider}: {Count} results, top='{Top}'",
                            query, providerName, searchResults.Count, searchResults[0].Title);
                    }
                    else
                    {
                        result.Error = "No results returned";
                        report.IssuesFound.Add($"Manual search returned 0 results: '{query}' via {providerName}");
                        logger.LogWarning("  Search '{Query}' via {Provider}: NO RESULTS", query, providerName);
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                report.IssuesFound.Add($"Manual search error: '{query}' via {providerName}: {ex.Message}");
                logger.LogError(ex, "  Search '{Query}' via {Provider}: ERROR", query, providerName);
            }

            report.ManualSearchResults.Add(result);
        }
    }

    // ── Vault display validation ─────────────────────────────────────────

    private static async Task ValidateVaultDisplayAsync(
        IRegistryRepository registryRepo,
        TestReport report,
        int stages,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);

        foreach (var item in allItems.Items)
        {
            // Fetch detail to get creator fields (author/director/artist)
            var detail = await registryRepo.GetDetailAsync(item.EntityId, ct);

            // Determine creator based on media type
            string? creator = item.MediaType.ToUpperInvariant() switch
            {
                "BOOKS" or "AUDIOBOOKS" => detail?.Author,
                "MOVIES" or "TV"        => detail?.Director,
                "MUSIC"                 => detail?.Author ?? item.Author,
                _                       => detail?.Author ?? item.Author,
            };

            var validStatuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Verified", "Provisional", "NeedsReview", "Identified", "Confirmed",
                "Registered", "InReview", "Quarantined", "Rejected",
            };

            var check = new VaultCheckResult
            {
                Title          = item.Title,
                MediaType      = item.MediaType,
                EntityId       = item.EntityId,
                HasCoverArt    = !string.IsNullOrWhiteSpace(item.CoverUrl),
                HasTitle       = !string.IsNullOrWhiteSpace(item.Title),
                HasCreator     = !string.IsNullOrWhiteSpace(creator),
                HasStatus      = !string.IsNullOrWhiteSpace(item.Status) && validStatuses.Contains(item.Status),
                HasRetailMatch = !string.IsNullOrWhiteSpace(item.RetailMatch) && item.RetailMatch != "none",
                HasWikidataQid = !string.IsNullOrWhiteSpace(item.WikidataQid),
                Status         = item.Status,
                CoverUrl       = item.CoverUrl,
                Creator        = creator,
                RetailMatch    = item.RetailMatch,
                WikidataQid    = item.WikidataQid,
            };
            report.VaultChecks.Add(check);

            if (!check.HasTitle)
                report.IssuesFound.Add($"Vault: '{item.FileName}' has no title");
            if (!check.HasCreator)
                report.IssuesFound.Add($"Vault: '{item.Title}' has no creator (author/director/artist)");
            if (!check.HasStatus)
                report.IssuesFound.Add($"Vault: '{item.Title}' has invalid or empty status: '{item.Status}'");
            if (!check.HasRetailMatch)
                logger.LogWarning("  Vault: '{Title}' missing retail match", item.Title);
            if (!check.HasCoverArt)
                logger.LogWarning("  Vault: '{Title}' missing cover art", item.Title);
            if (stages >= 12 && !check.HasWikidataQid)
                logger.LogWarning("  Vault: '{Title}' missing Wikidata QID (Stage 2 expected)", item.Title);
        }

        int passCount = report.VaultChecks.Count(v => v.Pass);
        logger.LogInformation("  Vault checks: {Pass}/{Total} items pass core validation",
            passCount, report.VaultChecks.Count);

        // ── Child entity validation (TV episodes, Music tracks) ──────────
        foreach (var item in allItems.Items)
        {
            if (string.IsNullOrWhiteSpace(item.WikidataQid)) continue;

            var detail = await registryRepo.GetDetailAsync(item.EntityId, ct);
            if (detail is null) continue;

            var canonMap = detail.CanonicalValues.ToDictionary(cv => cv.Key, cv => cv.Value, StringComparer.OrdinalIgnoreCase);

            if (item.MediaType.Equals("TV", StringComparison.OrdinalIgnoreCase))
            {
                var hasSeasonsStr = canonMap.GetValueOrDefault("season_count");
                var hasEpisodesStr = canonMap.GetValueOrDefault("episode_count");
                var hasChildren = canonMap.GetValueOrDefault("child_entities_json");
                int.TryParse(hasSeasonsStr, out var seasonCount);
                int.TryParse(hasEpisodesStr, out var episodeCount);

                if (seasonCount == 0)
                    report.IssuesFound.Add($"Child entities: TV '{item.Title}' has no season_count");
                if (episodeCount == 0)
                    logger.LogWarning("  Child entities: TV '{Title}' has no episode_count", item.Title);
                if (string.IsNullOrWhiteSpace(hasChildren))
                    report.IssuesFound.Add($"Child entities: TV '{item.Title}' has no child_entities_json");
                else
                    logger.LogInformation("  Child entities: TV '{Title}' — {Seasons} seasons, {Episodes} episodes",
                        item.Title, seasonCount, episodeCount);
            }
            else if (item.MediaType.Equals("Music", StringComparison.OrdinalIgnoreCase))
            {
                var hasTracksStr = canonMap.GetValueOrDefault("track_count");
                var hasChildren = canonMap.GetValueOrDefault("child_entities_json");
                int.TryParse(hasTracksStr, out var trackCount);

                if (!string.IsNullOrWhiteSpace(hasChildren))
                    logger.LogInformation("  Child entities: Music '{Title}' — {Tracks} tracks",
                        item.Title, trackCount);
            }
        }
    }

    // ── Stage gating validation ──────────────────────────────────────────

    private static async Task ValidateStageGatingAsync(
        IRegistryRepository registryRepo,
        TestReport report,
        int stages,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);

        if (stages == 1)
        {
            // Stage 1 only: NO items should have a Wikidata QID
            foreach (var item in allItems.Items)
            {
                bool hasQid = !string.IsNullOrWhiteSpace(item.WikidataQid);
                var result = new StageGatingResult
                {
                    Title  = item.Title,
                    Check  = "Stage 1 only: no Wikidata QID expected",
                    Pass   = !hasQid,
                    Detail = hasQid ? $"Unexpected QID: {item.WikidataQid}" : "Correctly absent",
                };
                report.StageGatingResults.Add(result);
                if (hasQid)
                    report.IssuesFound.Add($"Stage gating: '{item.Title}' has QID '{item.WikidataQid}' but Stage 2 should not have run");
            }
            logger.LogInformation("  Stage 1 gating: {Pass}/{Total} items correctly have no QID",
                report.StageGatingResults.Count(r => r.Pass), report.StageGatingResults.Count);
        }
        else if (stages >= 12)
        {
            // Stage 1+2: items WITH a retail match SHOULD have a QID;
            // items WITHOUT a retail match should NOT have a QID.
            foreach (var item in allItems.Items)
            {
                bool hasRetail = !string.IsNullOrWhiteSpace(item.RetailMatch) && item.RetailMatch != "none";
                bool hasQid = !string.IsNullOrWhiteSpace(item.WikidataQid);

                if (hasRetail)
                {
                    var result = new StageGatingResult
                    {
                        Title  = item.Title,
                        Check  = "Stage 2: retail match → QID expected",
                        Pass   = hasQid,
                        Detail = hasQid ? $"QID: {item.WikidataQid}" : "Missing QID despite retail match",
                    };
                    report.StageGatingResults.Add(result);
                    if (!hasQid)
                        logger.LogWarning("  Stage gating: '{Title}' has retail match but no QID", item.Title);
                }
                else
                {
                    var result = new StageGatingResult
                    {
                        Title  = item.Title,
                        Check  = "Stage 2: no retail match → QID not expected",
                        Pass   = true, // No retail match — QID absence is acceptable
                        Detail = hasQid ? $"Bonus QID: {item.WikidataQid}" : "No retail, no QID — expected",
                    };
                    report.StageGatingResults.Add(result);
                }
            }

            int pass = report.StageGatingResults.Count(r => r.Pass);
            logger.LogInformation("  Stage 1+2 gating: {Pass}/{Total} items pass gating checks",
                pass, report.StageGatingResults.Count);
        }
    }

    // ── Stage 3: Universe Enrichment (conditional) ───────────────────────

    private static async Task RunStage3EnrichmentAsync(
        HttpContext context,
        IRegistryRepository registryRepo,
        IDatabaseConnection db,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // Resolve the UniverseEnrichmentService from DI and trigger a manual sweep
            var universeService = context.RequestServices.GetService<UniverseEnrichmentService>();
            if (universeService is null)
            {
                report.IssuesFound.Add("Stage 3: UniverseEnrichmentService not registered in DI");
                logger.LogWarning("[Phase 7] UniverseEnrichmentService not found in DI — skipping Stage 3");
                return;
            }

            universeService.TriggerManualSweep();
            logger.LogInformation("[Phase 7] Stage 3 manual sweep triggered — waiting for completion...");

            // Poll for universe/parent hub creation (timeout: 3 minutes)
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
            int lastHubCount = 0;
            int stableCount = 0;
            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                int hubCount;
                using (var conn = db.CreateConnection())
                    hubCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM hubs WHERE parent_hub_id IS NOT NULL;");

                if (hubCount == lastHubCount && hubCount > 0) stableCount++;
                else stableCount = 0;
                lastHubCount = hubCount;

                logger.LogInformation("  Stage 3: {Hubs} hubs with parent, stable={Stable}", hubCount, stableCount);
                if (stableCount >= 3) break;
            }

            // Validate universe creation for known test data
            using (var conn = db.CreateConnection())
            {
                // Check for parent hubs (universes)
                var parentHubs = (await conn.QueryAsync<(string Id, string DisplayName, string? WikidataQid)>(
                    """
                    SELECT h.id, h.display_name, h.wikidata_qid
                    FROM hubs h
                    WHERE EXISTS (SELECT 1 FROM hubs child WHERE child.parent_hub_id = h.id)
                    """)).ToList();

                foreach (var ph in parentHubs)
                {
                    int childCount = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM hubs WHERE parent_hub_id = @id;",
                        new { id = ph.Id });

                    var universeResult = new UniverseResult
                    {
                        Name        = ph.DisplayName,
                        WikidataQid = ph.WikidataQid,
                        Found       = true,
                        SeriesCount = childCount,
                    };
                    // Count works under child hubs
                    int workCount = await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*) FROM works w
                        INNER JOIN hubs child ON w.hub_id = child.id
                        WHERE child.parent_hub_id = @id
                        """,
                        new { id = ph.Id });
                    universeResult.WorkCount = workCount;

                    report.UniverseResults.Add(universeResult);
                    logger.LogInformation("  Stage 3 Universe: '{Name}' — {Series} series, {Works} works, QID={Qid}",
                        ph.DisplayName, childCount, workCount, ph.WikidataQid ?? "none");
                }

                if (parentHubs.Count == 0)
                {
                    report.IssuesFound.Add("Stage 3: No parent hubs (universes) created after enrichment");
                    logger.LogWarning("[Phase 7] No universes found after Stage 3 enrichment");
                }
            }
        }
        catch (Exception ex)
        {
            report.IssuesFound.Add($"Stage 3 enrichment error: {ex.Message}");
            logger.LogError(ex, "[Phase 7] Stage 3 enrichment failed");
        }
    }

    // ── Check universes ───────────────────────────────────────────────────

    private static async Task CheckUniversesAsync(
        IRegistryRepository registryRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Check if items that should form universes got QIDs (prerequisite for universe formation)
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);

        // Look for Tolkien universe items (The Hobbit + Fellowship of the Ring)
        var tolkienItems = allItems.Items.Where(i =>
            i.Title.Contains("Hobbit", StringComparison.OrdinalIgnoreCase) ||
            i.Title.Contains("Fellowship", StringComparison.OrdinalIgnoreCase) ||
            i.Title.Contains("Lord of the Rings", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var tolkienResult = new UniverseResult
        {
            Name = "Tolkien (Middle-earth)",
            Found = tolkienItems.Any(i => !string.IsNullOrEmpty(i.WikidataQid)),
            WorkCount = tolkienItems.Count,
        };
        tolkienResult.WikidataQid = tolkienItems.FirstOrDefault(i => !string.IsNullOrEmpty(i.WikidataQid))?.WikidataQid;
        report.UniverseResults.Add(tolkienResult);

        // Look for Dune universe items
        var duneItems = allItems.Items.Where(i =>
            i.Title.Contains("Dune", StringComparison.OrdinalIgnoreCase) ||
            i.Title.Contains("Blade Runner", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var duneResult = new UniverseResult
        {
            Name = "Dune / Denis Villeneuve",
            Found = duneItems.Any(i => !string.IsNullOrEmpty(i.WikidataQid)),
            WorkCount = duneItems.Count,
        };
        duneResult.WikidataQid = duneItems.FirstOrDefault(i => !string.IsNullOrEmpty(i.WikidataQid))?.WikidataQid;
        report.UniverseResults.Add(duneResult);

        foreach (var u in report.UniverseResults)
        {
            logger.LogInformation("  Universe '{Name}': {Works} works, QID found={Found}",
                u.Name, u.WorkCount, u.Found);
        }
    }

    // ── Reconciliation pass ───────────────────────────────────────────────

    /// <summary>Dapper result row for the reconciliation SQL query.</summary>
    private sealed class WorkReconRow
    {
        public string TitleLower { get; set; } = "";
        public string MediaTypeLower { get; set; } = "";
        public string? WikidataQid { get; set; }
        public string? CuratorState { get; set; }
        public string? ReviewTrigger { get; set; }
    }

    /// <summary>
    /// Compares each seed fixture's declared expectation against the actual
    /// post-ingestion state of the corresponding Work row. Produces a
    /// <see cref="ReconciliationSummary"/> stored on the report.
    /// </summary>
    private static async Task RunReconciliationAsync(
        IDatabaseConnection db,
        TestReport report,
        IReadOnlyCollection<string> activeTypes,
        ILogger logger,
        CancellationToken ct)
    {
        // Filter expectations to only the media types actually exercised by this run
        // (e.g. skip Comics if the run was launched without comics in the types filter).
        var activeSet = new HashSet<string>(activeTypes, StringComparer.OrdinalIgnoreCase);
        var expectations = DevSeedEndpoints.GetAllExpectations()
            .Where(e => activeSet.Contains(e.MediaType))
            .ToList();
        var summary = new ReconciliationSummary { ExpectedTotal = expectations.Count };

        // Build a lookup of (title_lower, media_type_lower) → (wikidata_qid, curator_state, review_trigger)
        // from the live database. TV episodes share the same show title so we may have
        // duplicates — we treat any row for that (title, type) pair as "one Work" for
        // reconciliation purposes (first resolved row wins for identified check).

        // Dapper anonymous class for SQL projection
        IEnumerable<WorkReconRow> dbRows;
        using (var conn = db.CreateConnection())
        {
            // For reconciliation, we need to match the seed-supplied title against
            // ANY title we can find for the work — the file processor's claim,
            // the canonical value (which may have been overridden by Wikidata),
            // alternate_title claims, original_title, etc. We emit one row per
            // (work, title-source) pair via UNION so the C# index can lookup
            // the work from any of its known titles.
            dbRows = await conn.QueryAsync<WorkReconRow>(
                """
                WITH work_assets AS (
                    SELECT w.id AS work_id, w.media_type, w.curator_state, ma.id AS asset_id
                    FROM works w
                    INNER JOIN editions e ON e.work_id = w.id
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                ),
                work_qids AS (
                    SELECT wa.work_id,
                           (SELECT cv.value FROM canonical_values cv
                            WHERE cv.entity_id = wa.asset_id AND cv.key = 'wikidata_qid' LIMIT 1) AS qid
                    FROM work_assets wa
                ),
                work_reviews AS (
                    SELECT wa.work_id,
                           (SELECT rq.trigger FROM review_queue rq
                            WHERE rq.entity_id = wa.asset_id AND rq.status = 'Pending'
                            ORDER BY rq.created_at DESC LIMIT 1) AS trigger
                    FROM work_assets wa
                ),
                titles AS (
                    -- Canonical title
                    SELECT wa.work_id, wa.media_type, wa.curator_state, LOWER(cv.value) AS title_lower
                    FROM work_assets wa
                    INNER JOIN canonical_values cv ON cv.entity_id = wa.asset_id
                    WHERE cv.key IN ('title', 'original_title', 'show_name', 'series', 'episode_title', 'alternate_title', 'album')
                    UNION
                    -- File processor claim title (this is the seed-supplied title)
                    SELECT wa.work_id, wa.media_type, wa.curator_state, LOWER(mc.claim_value) AS title_lower
                    FROM work_assets wa
                    INNER JOIN metadata_claims mc ON mc.entity_id = wa.asset_id
                    WHERE mc.claim_key IN ('title', 'original_title', 'show_name', 'series', 'episode_title', 'alternate_title', 'album')
                )
                SELECT DISTINCT
                    t.title_lower               AS TitleLower,
                    LOWER(t.media_type)         AS MediaTypeLower,
                    (SELECT qid FROM work_qids WHERE work_id = t.work_id) AS WikidataQid,
                    t.curator_state             AS CuratorState,
                    (SELECT trigger FROM work_reviews WHERE work_id = t.work_id) AS ReviewTrigger
                FROM titles t
                WHERE t.title_lower IS NOT NULL AND t.title_lower <> ''
                """);
        }

        // Cover-art lookup built from the Vault display validation pass (Phase 4b).
        // Keyed by "title_lower|media_type_lower" → HasCoverArt flag. Used below
        // to enforce per-seed ExpectedCoverArt assertions.
        var coverArtByKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in report.VaultChecks)
        {
            string k = $"{check.Title.ToLowerInvariant()}|{check.MediaType.ToLowerInvariant()}";
            // Prefer the "has cover art" result if any duplicate key already exists —
            // matches the "first resolved wins" semantics used for QID below.
            if (!coverArtByKey.TryGetValue(k, out var existing) || (check.HasCoverArt && !existing))
                coverArtByKey[k] = check.HasCoverArt;
        }

        // Index by "title_lower|media_type_lower" → (wikidata_qid, curator_state, review_trigger)
        // When multiple rows share the same key (e.g. TV episodes, audiobook editions),
        // prefer rows that have a QID so "Identified" beats "Unresolved".
        var index = new Dictionary<string, (string? WikidataQid, string? CuratorState, string? ReviewTrigger)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var row in dbRows)
        {
            string key = $"{row.TitleLower}|{row.MediaTypeLower}";
            if (!index.TryGetValue(key, out var existing) ||
                (!string.IsNullOrWhiteSpace(row.WikidataQid) && string.IsNullOrWhiteSpace(existing.WikidataQid)))
            {
                index[key] = (WikidataQid: row.WikidataQid, CuratorState: row.CuratorState, ReviewTrigger: row.ReviewTrigger);
            }
        }

        foreach (var exp in expectations)
        {
            string titleLower = exp.Title.ToLowerInvariant();
            string mediaTypeLower = exp.MediaType.ToLowerInvariant();

            string expectedDesc = exp.ExpectIdentified
                ? (string.IsNullOrWhiteSpace(exp.ExpectedQid) ? "Identified" : $"Identified as {exp.ExpectedQid}")
                : $"InReview ({exp.ExpectedReviewTrigger ?? "any"})";

            // Not all active types were seeded — skip expectations for skipped types
            string typeKey = mediaTypeLower switch
            {
                "audiobooks" => "audiobooks",
                "movies"     => "movies",
                "tv"         => "tv",
                "music"      => "music",
                "comics"     => "comics",
                _            => "books",
            };
            if (report.SkippedTypes.ContainsKey(typeKey))
            {
                // Don't penalise for skipped types — exclude from reconciliation total
                summary.ExpectedTotal--;
                continue;
            }

            if (!index.TryGetValue($"{titleLower}|{mediaTypeLower}", out var actual))
            {
                // No matching Work row found
                var item = new ReconciliationItemResult
                {
                    Title          = exp.Title,
                    MediaType      = exp.MediaType,
                    Expected       = expectedDesc,
                    Actual         = "NotFound",
                    Classification = "NotFound",
                    Reason         = "No Work row found in database after ingestion",
                };
                summary.Mismatches.Add(item);
                summary.ByClassification["NotFound"]++;
                logger.LogWarning("[Reconciliation] NotFound: '{Title}' ({Type})", exp.Title, exp.MediaType);
                continue;
            }

            bool hasQid       = !string.IsNullOrWhiteSpace(actual.WikidataQid) &&
                                 !actual.WikidataQid!.StartsWith("NF", StringComparison.OrdinalIgnoreCase);
            bool hasReview    = !string.IsNullOrWhiteSpace(actual.ReviewTrigger);
            string actualTrigger = actual.ReviewTrigger ?? "";

            string actualDesc = hasQid   ? "Identified"
                              : hasReview ? $"InReview ({actualTrigger})"
                              : "Unresolved";

            string classification;
            if (exp.ExpectIdentified)
            {
                classification = hasQid ? "Match" : "UnexpectedReview";
            }
            else
            {
                if (hasReview)
                {
                    bool triggerMatches = string.IsNullOrWhiteSpace(exp.ExpectedReviewTrigger) ||
                        actualTrigger.Equals(exp.ExpectedReviewTrigger, StringComparison.OrdinalIgnoreCase);
                    classification = triggerMatches ? "Match" : "WrongTrigger";
                }
                else if (hasQid)
                {
                    classification = "UnexpectedIdentified";
                }
                else
                {
                    // Unresolved but expected InReview — treat as WrongTrigger
                    classification = "WrongTrigger";
                    actualDesc     = "Unresolved (no QID, no review entry)";
                }
            }

            // ── Layered strictness checks ────────────────────────────────────
            // Only run if the base classification was Match — a mismatched
            // review/identified state is a bigger problem than a QID/cover drift.
            if (classification == "Match" && exp.ExpectIdentified)
            {
                // WrongQid: seed declares a specific expected QID, actual differs.
                if (!string.IsNullOrWhiteSpace(exp.ExpectedQid) &&
                    !string.IsNullOrWhiteSpace(actual.WikidataQid) &&
                    !string.Equals(exp.ExpectedQid, actual.WikidataQid, StringComparison.OrdinalIgnoreCase))
                {
                    classification = "WrongQid";
                    actualDesc     = $"Identified as {actual.WikidataQid} (expected {exp.ExpectedQid})";
                }
                // MissingCoverArt: seed declares ExpectedCoverArt=true, Vault check
                // reports no cover URL for this title. Only applies to identified items
                // (we never expect cover art on placeholder/review-queue entries).
                else if (exp.ExpectedCoverArt)
                {
                    string coverKey = $"{titleLower}|{mediaTypeLower}";
                    if (coverArtByKey.TryGetValue(coverKey, out var hasCover) && !hasCover)
                    {
                        classification = "MissingCoverArt";
                        actualDesc     = $"Identified{(actual.WikidataQid is { Length: > 0 } q ? $" as {q}" : "")} but no cover art downloaded";
                    }
                }
            }

            summary.ByClassification[classification]++;
            if (classification == "Match")
            {
                summary.Matched++;
                logger.LogInformation("[Reconciliation] Match: '{Title}' ({Type}) — {Actual}",
                    exp.Title, exp.MediaType, actualDesc);
            }
            else
            {
                var item = new ReconciliationItemResult
                {
                    Title          = exp.Title,
                    MediaType      = exp.MediaType,
                    Expected       = expectedDesc,
                    Actual         = actualDesc,
                    Classification = classification,
                    Reason         = exp.ExpectedReason,
                };
                summary.Mismatches.Add(item);
                logger.LogWarning("[Reconciliation] {Class}: '{Title}' ({Type}) — expected={Expected}, actual={Actual}",
                    classification, exp.Title, exp.MediaType, expectedDesc, actualDesc);
            }
        }

        report.Reconciliation = summary;

        // Build the structured ReconciliationReport in parallel for JSON consumers.
        // The HTML report still pulls from ReconciliationSummary for backward compat.
        var structured = new ReconciliationReport
        {
            Total   = summary.ExpectedTotal,
            Matched = summary.Matched,
        };
        // Matched items are not retained as individual rows by ReconciliationSummary
        // (only mismatches). Synthesise placeholder rows for matches so the totals
        // line up; mismatches carry the full detail.
        for (int i = 0; i < summary.Matched; i++)
        {
            structured.Items.Add(new ReconciliationReportItem(
                FileName:        "(matched)",
                ExpectedStatus:  "Identified",
                ActualStatus:    "Identified",
                ExpectedTrigger: null,
                ActualTrigger:   null,
                Matched:         true,
                Reason:          null));
        }
        foreach (var mismatch in summary.Mismatches)
        {
            structured.Items.Add(new ReconciliationReportItem(
                FileName:        mismatch.Title,
                ExpectedStatus:  mismatch.Expected,
                ActualStatus:    mismatch.Actual,
                ExpectedTrigger: null,
                ActualTrigger:   null,
                Matched:         false,
                Reason:          mismatch.Reason));
        }
        report.ReconciliationReport = structured;

        logger.LogInformation("[Reconciliation] Complete: {Matched}/{Total} matched, {Mismatches} mismatches",
            summary.Matched, summary.ExpectedTotal, summary.Mismatches.Count);
    }

    // ── HTML Report Generator ─────────────────────────────────────────────

    private static string GenerateHtmlReport(TestReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Tuvima Integration Test — {report.Timestamp:yyyy-MM-dd HH:mm}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
            * { margin: 0; padding: 0; box-sizing: border-box; }
            body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                   background: #0f0f1a; color: #e0e0e0; padding: 24px; line-height: 1.5; }
            h1 { color: #C9922E; font-size: 24px; margin-bottom: 4px; }
            h2 { color: #C9922E; font-size: 18px; margin: 32px 0 12px; border-bottom: 1px solid rgba(201,146,46,0.3); padding-bottom: 8px; }
            h3 { color: #8B9DC3; font-size: 14px; margin: 16px 0 8px; text-transform: uppercase; letter-spacing: 0.05em; }
            .subtitle { color: rgba(255,255,255,0.5); font-size: 13px; margin-bottom: 24px; }
            .badge { display: inline-block; padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 600; }
            .badge-pass { background: rgba(93,202,165,0.15); color: #5DCAA5; border: 1px solid rgba(93,202,165,0.3); }
            .badge-fail { background: rgba(226,75,74,0.15); color: #E24B4A; border: 1px solid rgba(226,75,74,0.3); }
            .badge-warn { background: rgba(239,159,39,0.15); color: #EF9F27; border: 1px solid rgba(239,159,39,0.3); }
            .badge-info { background: rgba(96,165,250,0.15); color: #60A5FA; border: 1px solid rgba(96,165,250,0.3); }
            .badge-skip { background: rgba(148,163,184,0.15); color: #94A3B8; border: 1px solid rgba(148,163,184,0.3); }
            .summary-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 12px; margin: 16px 0; }
            .summary-card { background: rgba(255,255,255,0.04); border: 1px solid rgba(255,255,255,0.08); border-radius: 8px; padding: 16px; text-align: center; }
            .summary-card .num { font-size: 28px; font-weight: 700; }
            .summary-card .label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.1em; color: rgba(255,255,255,0.5); margin-top: 4px; }
            table { width: 100%; border-collapse: collapse; font-size: 13px; margin: 8px 0 16px; }
            th { text-align: left; padding: 8px 12px; border-bottom: 2px solid rgba(255,255,255,0.1);
                 color: rgba(255,255,255,0.6); font-size: 11px; text-transform: uppercase; letter-spacing: 0.05em; }
            td { padding: 8px 12px; border-bottom: 1px solid rgba(255,255,255,0.04); }
            tr:hover { background: rgba(255,255,255,0.02); }
            .status-identified { color: #5DCAA5; }
            .status-review { color: #EF9F27; }
            .status-failed { color: #E24B4A; }
            .status-unknown { color: rgba(255,255,255,0.4); }
            .issues-list { list-style: none; margin: 8px 0; }
            .issues-list li { padding: 6px 12px; margin: 4px 0; background: rgba(226,75,74,0.08);
                              border-left: 3px solid #E24B4A; border-radius: 4px; font-size: 13px; }
            .section { background: rgba(255,255,255,0.02); border: 1px solid rgba(255,255,255,0.06);
                       border-radius: 8px; padding: 16px; margin: 12px 0; }
            .media-type-header { display: flex; align-items: center; gap: 8px; margin-bottom: 8px; }
            .mt-dot { width: 10px; height: 10px; border-radius: 50%; display: inline-block; }
            .mono { font-family: 'SF Mono', 'Fira Code', monospace; font-size: 12px; }
            footer { margin-top: 48px; padding-top: 16px; border-top: 1px solid rgba(255,255,255,0.06);
                     color: rgba(255,255,255,0.3); font-size: 11px; text-align: center; }
        """);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        string overallBadge = report.IssuesFound.Count == 0
            ? "<span class=\"badge badge-pass\">ALL PASS</span>"
            : $"<span class=\"badge badge-warn\">{report.IssuesFound.Count} ISSUES</span>";
        sb.AppendLine($"<h1>Tuvima Library — Integration Test Report {overallBadge}</h1>");
        string stageBadge = report.StagesLevel switch
        {
            1   => "<span class=\"badge badge-info\">Stage 1</span>",
            12  => "<span class=\"badge badge-info\">Stage 1+2</span>",
            123 => "<span class=\"badge badge-info\">Stage 1+2+3</span>",
            _   => $"<span class=\"badge badge-warn\">Stage {report.StagesLevel}</span>",
        };
        string typesBadge = report.ActiveTypes.Count == AllTestableTypes.Length
            ? "<span class=\"badge badge-pass\">All Types</span>"
            : $"<span class=\"badge badge-info\">{string.Join(", ", report.ActiveTypes.OrderBy(t => t))}</span>";
        sb.AppendLine($"<p class=\"subtitle\">{report.Timestamp:yyyy-MM-dd HH:mm:ss UTC} · Duration: {report.TotalDuration.TotalSeconds:F1}s · Ingestion: {report.IngestionDuration.TotalSeconds:F1}s · {stageBadge} · {typesBadge}</p>");

        // Summary cards
        sb.AppendLine("<div class=\"summary-grid\">");
        SummaryCard(sb, report.TotalFilesSeeded.ToString(), "Files Seeded", "#60A5FA");
        SummaryCard(sb, report.TotalItems.ToString(), "Items Detected", "#8B9DC3");
        SummaryCard(sb, report.TotalIdentified.ToString(), "Identified", "#5DCAA5");
        SummaryCard(sb, report.TotalNeedsReview.ToString(), "Needs Review", "#EF9F27");
        SummaryCard(sb, report.TotalFailed.ToString(), "Failed", "#E24B4A");
        SummaryCard(sb, report.ManualSearchResults.Count(s => s.Pass).ToString() + "/" + report.ManualSearchResults.Count, "Search Tests", "#A78BFA");
        SummaryCard(sb, report.VaultChecks.Count(v => v.Pass).ToString() + "/" + report.VaultChecks.Count, "Vault Checks", "#22D3EE");
        SummaryCard(sb, report.StageGatingResults.Count(g => g.Pass).ToString() + "/" + report.StageGatingResults.Count, "Stage Gating", "#FB923C");
        if (report.Reconciliation is not null)
            SummaryCard(sb, report.Reconciliation.Matched + "/" + report.Reconciliation.ExpectedTotal, "Reconciliation", "#F472B6");
        if (report.SkippedTypes.Count > 0)
            SummaryCard(sb, report.SkippedTypes.Count.ToString(), "Skipped Types", "#94A3B8");
        sb.AppendLine("</div>");

        // Provider health section
        if (report.ProviderHealth.Count > 0)
        {
            sb.AppendLine("<h2>Provider Health</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Provider</th><th>Status</th><th>Media Types</th></tr>");
            foreach (var (provider, healthy) in report.ProviderHealth.OrderBy(p => p.Key))
            {
                string badge = healthy
                    ? "<span class=\"badge badge-pass\">HEALTHY</span>"
                    : "<span class=\"badge badge-fail\">UNAVAILABLE</span>";
                string types = ProviderToTypes.TryGetValue(provider, out var ts) ? string.Join(", ", ts) : "—";
                sb.AppendLine($"<tr><td>{Esc(provider)}</td><td>{badge}</td><td>{Esc(types)}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Skipped types section
        if (report.SkippedTypes.Count > 0)
        {
            sb.AppendLine("<h2>Skipped Media Types</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Media Type</th><th>Reason</th></tr>");
            foreach (var (type, reason) in report.SkippedTypes.OrderBy(s => s.Key))
                sb.AppendLine($"<tr><td>{Esc(type)}</td><td><span class=\"badge badge-skip\">{Esc(reason)}</span></td></tr>");
            sb.AppendLine("</table>");
        }

        // Issues section (if any)
        if (report.IssuesFound.Count > 0)
        {
            sb.AppendLine("<h2>Issues Found</h2>");
            sb.AppendLine("<ul class=\"issues-list\">");
            foreach (var issue in report.IssuesFound)
                sb.AppendLine($"  <li>{Esc(issue)}</li>");
            sb.AppendLine("</ul>");
        }

        // Per media type results
        sb.AppendLine("<h2>Results by Media Type</h2>");
        var mtColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Books"] = "#5DCAA5", ["Audiobooks"] = "#A78BFA", ["Movies"] = "#60A5FA",
            ["TV"] = "#FBBF24", ["Music"] = "#22D3EE", ["Podcasts"] = "#FB923C", ["Comics"] = "#7C4DFF",
        };

        foreach (var mt in report.MediaTypeResults.OrderByDescending(m => m.Count))
        {
            string color = mtColors.GetValueOrDefault(mt.MediaType, "#888");
            string badge = mt.Count == 0 ? "<span class=\"badge badge-fail\">MISSING</span>"
                : mt.Failed > 0 ? "<span class=\"badge badge-fail\">FAILURES</span>"
                : mt.NeedsReview > 0 ? "<span class=\"badge badge-warn\">REVIEW</span>"
                : "<span class=\"badge badge-pass\">OK</span>";

            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"<div class=\"media-type-header\"><span class=\"mt-dot\" style=\"background:{color}\"></span><strong>{Esc(mt.MediaType)}</strong> — {mt.Count} items ({mt.Identified} identified, {mt.NeedsReview} review, {mt.Failed} failed) {badge}</div>");

            if (mt.Items.Count > 0)
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Title</th><th>Author</th><th>Year</th><th>Status</th><th>Confidence</th><th>Retail Match</th><th>QID</th><th>File</th></tr>");
                foreach (var item in mt.Items.OrderBy(i => i.Title))
                {
                    string statusClass = StatusClass(item.Status);
                    sb.AppendLine($"<tr><td>{Esc(item.Title)}</td><td>{Esc(item.Author ?? "—")}</td><td>{Esc(item.Year ?? "—")}</td>" +
                        $"<td class=\"{statusClass}\">{Esc(item.Status)}{(item.ReviewTrigger is not null ? $" <span class=\"mono\">({Esc(item.ReviewTrigger)})</span>" : "")}</td>" +
                        $"<td>{item.Confidence:P0}</td><td>{Esc(item.RetailMatch ?? "none")}</td>" +
                        $"<td class=\"mono\">{Esc(item.WikidataQid ?? "—")}</td><td class=\"mono\" style=\"max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap\">{Esc(item.FileName)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }
            sb.AppendLine("</div>");
        }

        // Skipped media types as greyed-out sections
        foreach (var (type, reason) in report.SkippedTypes.OrderBy(s => s.Key))
        {
            // Don't double-render if it already appeared in results
            if (report.MediaTypeResults.Any(r => r.MediaType.Equals(type, StringComparison.OrdinalIgnoreCase))) continue;
            sb.AppendLine("<div class=\"section\" style=\"opacity: 0.5;\">");
            sb.AppendLine($"<div class=\"media-type-header\"><span class=\"mt-dot\" style=\"background:#94A3B8\"></span><strong>{Esc(type)}</strong> <span class=\"badge badge-skip\">SKIPPED — {Esc(reason)}</span></div>");
            sb.AppendLine("</div>");
        }

        // Manual search results
        sb.AppendLine("<h2>Manual Search Tests</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Query</th><th>Provider</th><th>Media Type</th><th>Results</th><th>Top Result</th><th>Confidence</th><th>Status</th></tr>");
        foreach (var s in report.ManualSearchResults)
        {
            string badge = s.Pass ? "<span class=\"badge badge-pass\">PASS</span>" : $"<span class=\"badge badge-fail\">FAIL: {Esc(s.Error ?? "unknown")}</span>";
            sb.AppendLine($"<tr><td>{Esc(s.Query)}</td><td>{Esc(s.ProviderName)}</td><td>{Esc(s.MediaType)}</td>" +
                $"<td>{s.ResultCount}</td><td>{Esc(s.TopResultTitle ?? "—")}</td><td>{s.TopResultConfidence:P0}</td><td>{badge}</td></tr>");
        }
        sb.AppendLine("</table>");

        // Universe enrichment results
        sb.AppendLine("<h2>Universe Enrichment</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Universe</th><th>Works Found</th><th>QID Present</th><th>Sample QID</th><th>Status</th></tr>");
        foreach (var u in report.UniverseResults)
        {
            string badge = u.Found ? "<span class=\"badge badge-pass\">QID RESOLVED</span>" : "<span class=\"badge badge-warn\">NO QID YET</span>";
            sb.AppendLine($"<tr><td>{Esc(u.Name)}</td><td>{u.WorkCount}</td><td>{u.Found}</td><td class=\"mono\">{Esc(u.WikidataQid ?? "—")}</td><td>{badge}</td></tr>");
        }
        sb.AppendLine("</table>");

        // Vault Display Validation
        if (report.VaultChecks.Count > 0)
        {
            int vaultPass = report.VaultChecks.Count(v => v.Pass);
            string vaultBadge = vaultPass == report.VaultChecks.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.VaultChecks.Count - vaultPass} ISSUES</span>";
            sb.AppendLine($"<h2>Vault Display Validation {vaultBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Cover Art</th><th>Title</th><th>Creator</th><th>Status</th><th>Retail Match</th><th>QID</th></tr>");
            foreach (var v in report.VaultChecks.OrderBy(v => v.MediaType).ThenBy(v => v.Title))
            {
                string Check(bool ok) => ok ? "<span style=\"color:#5DCAA5\">&#x2713;</span>" : "<span style=\"color:#E24B4A\">&#x2717;</span>";
                sb.AppendLine($"<tr><td>{Esc(v.Title)}</td><td>{Esc(v.MediaType)}</td>" +
                    $"<td>{Check(v.HasCoverArt)}</td><td>{Check(v.HasTitle)}</td><td>{Check(v.HasCreator)}</td>" +
                    $"<td>{Check(v.HasStatus)} <span class=\"mono\">{Esc(v.Status ?? "")}</span></td>" +
                    $"<td>{Check(v.HasRetailMatch)} <span class=\"mono\">{Esc(v.RetailMatch ?? "")}</span></td>" +
                    $"<td>{Check(v.HasWikidataQid)} <span class=\"mono\">{Esc(v.WikidataQid ?? "")}</span></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Stage Gating Validation
        if (report.StageGatingResults.Count > 0)
        {
            int gatePass = report.StageGatingResults.Count(r => r.Pass);
            string gateBadge = gatePass == report.StageGatingResults.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.StageGatingResults.Count - gatePass} ISSUES</span>";
            sb.AppendLine($"<h2>Stage Gating Validation {gateBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Check</th><th>Result</th><th>Detail</th></tr>");
            foreach (var g in report.StageGatingResults.OrderBy(g => g.Title))
            {
                string resultBadge = g.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                sb.AppendLine($"<tr><td>{Esc(g.Title)}</td><td>{Esc(g.Check)}</td><td>{resultBadge}</td><td class=\"mono\">{Esc(g.Detail ?? "")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Reconciliation section
        if (report.Reconciliation is not null)
        {
            var recon = report.Reconciliation;
            int mismatches = recon.Mismatches.Count;
            string reconBadge = mismatches == 0
                ? "<span class=\"badge badge-pass\">ALL MATCH</span>"
                : $"<span class=\"badge badge-fail\">{mismatches} MISMATCH{(mismatches == 1 ? "" : "ES")}</span>";
            sb.AppendLine($"<h2>Reconciliation — Seed Expectations vs. Pipeline Outcomes {reconBadge}</h2>");
            sb.AppendLine($"<p class=\"subtitle\">{recon.Matched}/{recon.ExpectedTotal} seed fixtures matched their expected outcome · ");
            sb.AppendLine(string.Join(" · ", recon.ByClassification
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{Esc(kv.Key)}: {kv.Value}")));
            sb.AppendLine("</p>");

            // ── Matched section (collapsed by default) ──────────────────────
            var matched = recon.ExpectedTotal - mismatches;
            sb.AppendLine($"<details><summary style=\"cursor:pointer;color:#5DCAA5;font-weight:600\">&#x2713; Matched Expected ({matched})</summary>");
            // Reconstruct matched items by re-running the DB data (we only stored mismatches)
            // Instead enumerate expectations again to build the full set, but since we only have
            // mismatches stored, derive matched count via total - mismatches. Show a simple
            // summary table for mismatches and a collapsed table for everything else.
            sb.AppendLine("<p style=\"color:rgba(255,255,255,0.4);font-size:12px;padding:8px 0\">" +
                $"{matched} item(s) produced the outcome declared in their seed fixture.</p>");
            sb.AppendLine("</details>");

            // ── Mismatch sections by classification ─────────────────────────
            var classOrder = new[] { "UnexpectedReview", "UnexpectedIdentified", "WrongTrigger", "WrongQid", "MissingCoverArt", "NotFound" };
            var classLabels = new Dictionary<string, string>
            {
                ["UnexpectedReview"]    = "Unexpected Review (expected Identified, got InReview)",
                ["UnexpectedIdentified"] = "Unexpected Identified (expected InReview, got Identified)",
                ["WrongTrigger"]        = "Wrong Trigger (in review, but wrong trigger)",
                ["WrongQid"]            = "Wrong Wikidata QID (resolved, but to the wrong entity)",
                ["MissingCoverArt"]     = "Missing Cover Art (identified, but no cover downloaded)",
                ["NotFound"]            = "Not Found (no Work row in database)",
            };
            var classColors = new Dictionary<string, string>
            {
                ["UnexpectedReview"]    = "#E24B4A",
                ["UnexpectedIdentified"] = "#E24B4A",
                ["WrongTrigger"]        = "#EF9F27",
                ["WrongQid"]            = "#E24B4A",
                ["MissingCoverArt"]     = "#EF9F27",
                ["NotFound"]            = "#94A3B8",
            };

            foreach (var cls in classOrder)
            {
                var items = recon.Mismatches.Where(m => m.Classification == cls).ToList();
                if (items.Count == 0) continue;

                string color = classColors.GetValueOrDefault(cls, "#888");
                sb.AppendLine($"<details open><summary style=\"cursor:pointer;color:{color};font-weight:600\">&#x2717; {Esc(classLabels[cls])} ({items.Count})</summary>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Expected</th><th>Actual</th><th>Reason</th></tr>");
                foreach (var item in items.OrderBy(i => i.MediaType).ThenBy(i => i.Title))
                {
                    sb.AppendLine($"<tr>" +
                        $"<td>{Esc(item.Title)}</td>" +
                        $"<td>{Esc(item.MediaType)}</td>" +
                        $"<td class=\"mono\">{Esc(item.Expected)}</td>" +
                        $"<td class=\"mono\" style=\"color:{color}\">{Esc(item.Actual)}</td>" +
                        $"<td style=\"color:rgba(255,255,255,0.5);font-size:12px\">{Esc(item.Reason ?? "—")}</td>" +
                        $"</tr>");
                }
                sb.AppendLine("</table>");
                sb.AppendLine("</details>");
            }
        }

        // Footer
        sb.AppendLine($"<footer>Generated by Tuvima Library Integration Test Harness · {report.Timestamp:yyyy-MM-dd HH:mm:ss}</footer>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // ── HTML helpers ──────────────────────────────────────────────────────

    private static void SummaryCard(StringBuilder sb, string num, string label, string color)
    {
        sb.AppendLine($"<div class=\"summary-card\"><div class=\"num\" style=\"color:{color}\">{num}</div><div class=\"label\">{label}</div></div>");
    }

    private static string Esc(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string StatusClass(string? status)
    {
        var s = (status ?? "").ToUpperInvariant();
        if (s.Contains("IDENTIFIED") || s.Contains("CONFIRMED") || s.Contains("REGISTERED")) return "status-identified";
        if (s.Contains("REVIEW")) return "status-review";
        if (s.Contains("FAIL") || s.Contains("QUARANTINE")) return "status-failed";
        return "status-unknown";
    }

    // ── Seed data accessors (mirrors DevSeedEndpoints private data) ──────
    // These provide the seed definitions without duplicating the data.

    internal sealed record SeedBookInfo(string Title, string Author, string Isbn, int Year, string Description,
        string? Publisher = null, string Language = "en", string[]? AdditionalAuthors = null,
        string? Series = null, int? SeriesPosition = null);

    internal sealed record SeedAudiobookInfo(string Title, string Artist, string Narrator, int Year,
        string Language = "eng", string? Series = null, int? SeriesPosition = null, string? Asin = null);

    internal sealed record SeedVideoInfo(string Title, string? Director, int Year, string MediaType,
        string? Series = null, int? SeasonNumber = null, int? EpisodeNumber = null);

    internal sealed record SeedMusicInfo(string Title, string Artist, string? Album = null,
        int Year = 0, string? Genre = null, int? TrackNumber = null);

    internal static SeedBookInfo[] DevSeedEndpoints_SeedBooks() =>
    [
        new("Dune", "Frank Herbert", "9780441013593", 1965, "Set on the desert planet Arrakis."),
        new("Project Hail Mary", "Andy Weir", "9780593135204", 2021, "Ryland Grace is the sole survivor on a desperate mission."),
        new("The Hobbit", "J.R.R. Tolkien", "9780547928227", 1937, "Bilbo Baggins is a hobbit who enjoys a comfortable life."),
        new("Leviathan Wakes", "James S. A. Corey", "9780316129084", 2011, "Humanity has colonized the solar system.",
            Series: "The Expanse", SeriesPosition: 1),
        new("The Shining", "Stephen King", "9780307743657", 1977, "Jack Torrance's new job at the Overlook Hotel."),
        new("Harry Potter and the Philosopher's Stone", "J.K. Rowling", "9780747532699", 1997, "Harry Potter has never heard of Hogwarts.",
            Series: "Harry Potter", SeriesPosition: 1),
        new("Harry Potter and the Chamber of Secrets", "J.K. Rowling", "9780747538486", 1998, "Harry Potter's summer has included the worst birthday ever.",
            Series: "Harry Potter", SeriesPosition: 2),
        new("The Fellowship of the Ring", "J.R.R. Tolkien", "9780547928210", 1954, "In ancient times the Rings of Power were crafted.",
            Series: "The Lord of the Rings", SeriesPosition: 1),
        new("Good Omens", "Terry Pratchett", "9780060853983", 1990, "The world will end on a Saturday.", AdditionalAuthors: ["Neil Gaiman"]),
        new("Neuromancer", "William Gibson", "9780441569595", 1984, "The sky above the port was the color of television."),
        new("The Road", "Cormac McCarthy", "9780307387899", 2006, "A father and his son walk alone through burned America."),
    ];

    internal static SeedAudiobookInfo[] DevSeedEndpoints_SeedAudiobooks() =>
    [
        new("Dune", "Frank Herbert", "Simon Vance", 1965, Series: "Dune Chronicles", SeriesPosition: 1),
        new("Project Hail Mary", "Andy Weir", "Ray Porter", 2021),
        new("The Hobbit", "J.R.R. Tolkien", "Andy Serkis", 1937),
        new("Harry Potter and the Philosopher's Stone", "J.K. Rowling", "Stephen Fry", 1997, Series: "Harry Potter", SeriesPosition: 1),
        new("The Fellowship of the Ring", "J.R.R. Tolkien", "Rob Inglis", 1954, Series: "The Lord of the Rings", SeriesPosition: 1),
        new("Neuromancer", "William Gibson", "Robertson Dean", 1984),
    ];

    internal static SeedVideoInfo[] DevSeedEndpoints_SeedVideos() =>
    [
        new("Blade Runner 2049", "Denis Villeneuve", 2017, "Movie"),
        new("The Matrix", "Lana Wachowski", 1999, "Movie"),
        new("Interstellar", "Christopher Nolan", 2014, "Movie"),
        new("Spirited Away", "Hayao Miyazaki", 2001, "Movie"),
        new("The Shawshank Redemption", "Frank Darabont", 1994, "Movie"),
        new("Breaking Bad", null, 2008, "TV", Series: "Breaking Bad", SeasonNumber: 1, EpisodeNumber: 1),
        new("Breaking Bad", null, 2008, "TV", Series: "Breaking Bad", SeasonNumber: 1, EpisodeNumber: 2),
        new("The Expanse", null, 2015, "TV", Series: "The Expanse", SeasonNumber: 1, EpisodeNumber: 1),
        new("Shogun", null, 2024, "TV", Series: "Shogun", SeasonNumber: 1, EpisodeNumber: 1),
    ];

    internal static SeedMusicInfo[] DevSeedEndpoints_SeedMusic() =>
    [
        new("Bohemian Rhapsody", "Queen", Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 11),
        new("Clair de Lune", "Claude Debussy", Album: "Suite bergamasque", Year: 1905, Genre: "Classical", TrackNumber: 3),
        new("Lose Yourself", "Eminem", Album: "8 Mile: Music from and Inspired by the Motion Picture", Year: 2002, Genre: "Hip-Hop", TrackNumber: 1),
        new("Nuvole Bianche", "Ludovico Einaudi", Album: "Una Mattina", Year: 2004, Genre: "Classical", TrackNumber: 6),
        new("Across the Stars", "John Williams", Album: "Star Wars: Attack of the Clones", Year: 2002, Genre: "Soundtrack", TrackNumber: 3),
        new("You're My Best Friend", "Queen", Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 4),
        new("Death on Two Legs", "Queen", Album: "A Night at the Opera", Year: 1975, Genre: "Rock", TrackNumber: 1),
        new("Under Pressure", "Queen & David Bowie", Album: "Hot Space", Year: 1982, Genre: "Rock", TrackNumber: 11),
        new("Stan", "Eminem", Album: "The Marshall Mathers LP", Year: 2000, Genre: "Hip-Hop", TrackNumber: 3),
        new("La Vie en rose", "Édith Piaf", Album: "La Vie en rose", Year: 1947, Genre: "Chanson", TrackNumber: 1),
        new("Für Elise", "Ludwig van Beethoven", Album: "Beethoven: Piano Pieces", Year: 1810, Genre: "Classical", TrackNumber: 1),
        new("99 Luftballons", "Nena", Album: "99 Luftballons", Year: 1983, Genre: "New Wave", TrackNumber: 1),
        new("Yesterday", "The Beatles", Album: "Help!", Year: 1965, Genre: "Pop", TrackNumber: 13),
        new("Imagine", "John Lennon", Album: "Imagine", Year: 1971, Genre: "Pop", TrackNumber: 1),
        new("The Imperial March", "John Williams", Album: "Star Wars: The Empire Strikes Back", Year: 1980, Genre: "Soundtrack", TrackNumber: 3),
        new("In the Hall of the Mountain King", "Edvard Grieg", Album: "Peer Gynt Suite No. 1", Year: 1875, Genre: "Classical", TrackNumber: 4),
        new("4'33\"", "John Cage", Album: "John Cage: 4'33\"", Year: 1952, Genre: "Avant-Garde", TrackNumber: 1),
        new("MMMBop", "Hanson", Album: "Middle of Nowhere", Year: 1997, Genre: "Pop", TrackNumber: 1),
        new("Take Five", "Dave Brubeck", Album: "Time Out", Year: 1959, Genre: "Jazz", TrackNumber: 4),
        new("Smells Like Teen Spirit", "Nirvana", Album: "Nevermind", Year: 1991, Genre: "Grunge", TrackNumber: 1),
    ];

    internal sealed record SeedComicInfo(string Title, string? Writer = null,
        string? Series = null, int? Number = null, int Year = 0,
        string? Genre = null, string? Summary = null, string? Publisher = null,
        string? Penciller = null);

    internal static SeedComicInfo[] DevSeedEndpoints_SeedComics() =>
    [
        new("Batman: Year One Part 1", Writer: "Frank Miller",
            Series: "Batman", Number: 404, Year: 1987, Genre: "Superhero",
            Summary: "Bruce Wayne returns to Gotham City after years abroad.",
            Publisher: "DC Comics", Penciller: "David Mazzucchelli"),
        new("Saga Chapter One", Writer: "Brian K. Vaughan",
            Series: "Saga", Number: 1, Year: 2012, Genre: "Science Fiction, Fantasy",
            Summary: "A new epic from the creators of Y: The Last Man.",
            Publisher: "Image Comics", Penciller: "Fiona Staples"),
        new("The Sandman: Sleep of the Just", Writer: "Neil Gaiman",
            Series: "The Sandman", Number: 1, Year: 1989, Genre: "Fantasy, Horror",
            Summary: "Morpheus, the King of Dreams, is captured and held prisoner for 70 years.",
            Publisher: "DC Comics/Vertigo", Penciller: "Sam Kieth"),
        new("Akira Vol 1", Writer: "Katsuhiro Otomo",
            Series: "Akira", Number: 1, Year: 1982, Genre: "Science Fiction",
            Summary: "In the year 2019, Neo-Tokyo has risen from the ashes of World War III.",
            Publisher: "Kodansha", Penciller: "Katsuhiro Otomo"),
    ];

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void WipeDirectoryContents(string dirPath)
    {
        var dir = new DirectoryInfo(dirPath);
        foreach (FileInfo file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            try { file.Attributes = FileAttributes.Normal; file.Delete(); } catch { }
        }
        foreach (DirectoryInfo sub in dir.GetDirectories())
        {
            try { sub.Delete(recursive: true); } catch { }
        }
    }

    private static void WipeFilesOnly(string dirPath)
    {
        var dir = new DirectoryInfo(dirPath);
        foreach (FileInfo file in dir.GetFiles())
        {
            try { file.Attributes = FileAttributes.Normal; file.Delete(); } catch { }
        }
    }

    private static string SanitizeFileName(string title)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (char c in title) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
