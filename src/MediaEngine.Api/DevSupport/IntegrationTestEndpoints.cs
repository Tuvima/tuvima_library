using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Domain;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Ingestion;
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
        public List<LibraryCheckResult> LibraryChecks { get; set; } = [];
        public List<FileSystemCheckResult> FileSystemChecks { get; set; } = [];
        public List<WatchFolderCheckResult> WatchFolderChecks { get; set; } = [];
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

    /// <summary>Per-item library display validation result.</summary>
    private sealed class LibraryCheckResult
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
        public string? ExpectedRetailProvider { get; set; }
        public string? ActualRetailProvider { get; set; }
        public bool HasExpectedRetailProvider { get; set; } = true;
        public bool RequiresCreator =>
            !string.Equals(MediaType, "TV", StringComparison.OrdinalIgnoreCase);
        public bool RequiresRetailProvider =>
            string.Equals(RetailMatch, "matched", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(ExpectedRetailProvider);
        public bool Pass =>
            HasTitle
            && (!RequiresCreator || HasCreator)
            && HasStatus
            && HasRetailMatch
            && (!RequiresRetailProvider || HasExpectedRetailProvider);
    }

    /// <summary>On-disk placement and artwork validation result.</summary>
    private sealed class FileSystemCheckResult
    {
        public string Title { get; set; } = "";
        public string MediaType { get; set; } = "";
        public string Status { get; set; } = "";
        public string? ExpectedLocation { get; set; }
        public string? ExpectedRelativePath { get; set; }
        public string? ActualFilePath { get; set; }
        public string? ActualDisplayPath { get; set; }
        public bool FileExists { get; set; }
        public bool InLibraryRoot { get; set; }
        public bool InStagingRoot { get; set; }
        public bool LocationMatchesExpectation { get; set; }
        public bool RequiresTemplateMatch { get; set; }
        public bool PathMatchesTemplate { get; set; }
        public bool ExpectedCoverArt { get; set; }
        public bool RequiresSidecarArtwork { get; set; }
        public bool HasPoster { get; set; }
        public bool HasPosterThumb { get; set; }
        public bool HasHero { get; set; }
        public bool RequiresStoredArtwork { get; set; }
        public bool HasStoredCover { get; set; }
        public bool HasStoredCoverThumb { get; set; }
        public bool HasStoredHero { get; set; }
        public bool HasStoredBackdrop { get; set; }
        public bool HasStoredLogo { get; set; }
        public bool HasStoredBanner { get; set; }
        public string? Detail { get; set; }
        public bool Pass =>
            FileExists
            && LocationMatchesExpectation
            && (!RequiresTemplateMatch || PathMatchesTemplate)
            && (!RequiresSidecarArtwork || (HasPoster && HasPosterThumb && HasHero))
            && (!RequiresStoredArtwork || (HasStoredCover && HasStoredCoverThumb && HasStoredHero));
    }

    /// <summary>Checks that watch/source folders were drained after ingestion.</summary>
    private sealed class WatchFolderCheckResult
    {
        public string Directory { get; set; } = "";
        public int RemainingMediaFiles { get; set; }
        public int IgnoredExpectedStagingFiles { get; set; }
        public bool Pass => RemainingMediaFiles == 0;
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
            ["WrongProvider"] = 0,
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
        public string? FilePath { get; set; }
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
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".webm", ".ts",
        ".m4b", ".mp3", ".flac", ".m4a", ".ogg", ".opus", ".wav", ".aac",
        ".epub", ".pdf",
        ".cbz", ".cbr", ".cb7",
    };

    private static readonly Dictionary<string, string[]> ProviderToTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = ["books", "audiobooks", "music"],
        ["tmdb"]        = ["movies", "tv"],
        ["comicvine"]   = ["comics"],
    };

    private static readonly Dictionary<string, string> ProviderHealthUrls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apple_api"]   = "https://itunes.apple.com/search?term=test&limit=1",
        ["tmdb"]        = "https://api.themoviedb.org/3/configuration",
        ["comicvine"]   = "https://comicvine.gamespot.com/api/search/?query=batman&resources=issue&limit=1&format=json&api_key=placeholder",
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
        IIdentityJobRepository identityJobRepo,
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
        bool ingestionComplete = await WaitForIngestionAsync(
            db,
            identityJobRepo,
            logger,
            TimeSpan.FromMinutes(8),
            report.TotalFilesSeeded,
            ct);
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
            await ValidateVaultDisplayAsync(db, registryRepo, report, stages, logger, ct);

        // ── Phase 4c: File system and artwork validation ───────────────────
        logger.LogInformation("[Phase 4c] File system and artwork validation...");
        await ValidateFileSystemAsync(db, options, configLoader, registryRepo, report, loggerFactory, logger, ct);

        // ── Phase 4d: Stage Gating Validation ───────────────────────────
        logger.LogInformation("[Phase 4d] Stage gating validation...");
        await ValidateStageGatingAsync(registryRepo, report, stages, logger, ct);

        // ── Phase 4e: Reconciliation — expected vs. actual outcomes ─────────
        logger.LogInformation("[Phase 4e] Running reconciliation pass...");
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
    //
    // Delegates entirely to DevSeedEndpoints.SeedAllAsync, which is the single
    // source of truth for fixture seeding. This wrapper exists only so the
    // integration-test Phase 2 can call it without the HTTP endpoint plumbing.
    //
    // CRITICAL: do not reintroduce inline seed arrays here. They will silently
    // drift from the canonical DevSeedEndpoints data and cause "NotFound" false
    // negatives in the reconciliation pass.

    private static Task<int> SeedInternalAsync(
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        HashSet<string> activeTypes,
        ILogger logger)
        => DevSeedEndpoints.SeedAllAsync(options, configLoader, activeTypes, logger);

    // ── Wait for ingestion ────────────────────────────────────────────────

    private static async Task<bool> WaitForIngestionAsync(
        IDatabaseConnection db,
        IIdentityJobRepository identityJobRepo,
        ILogger logger,
        TimeSpan timeout,
        int expectedCount,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        int lastAssetCount = -1;
        int lastResolvedCount = -1;
        int lastClaimCount = -1;
        int lastPendingCount = -1;
        int lastActiveJobCount = -1;
        int stableSnapshots = 0;
        bool sawExpectedAssetCount = false;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct);

            int assetCount;
            int totalWorks;
            int resolvedCount;
            int claimCount;
            int pendingCount;

            using (var conn = db.CreateConnection())
            {
                assetCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM media_assets;");
                totalWorks = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM works;");
                resolvedCount = conn.ExecuteScalar<int>("""
                    SELECT COUNT(DISTINCT w.id) FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN review_queue rq ON rq.entity_id = ma.id AND rq.status = 'Pending'
                    WHERE w.wikidata_qid IS NOT NULL OR rq.id IS NOT NULL OR w.curator_state IS NOT NULL
                    """);
                claimCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM metadata_claims;");
                pendingCount = conn.ExecuteScalar<int>("""
                    SELECT COUNT(DISTINCT w.id) FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN review_queue rq ON rq.entity_id = ma.id AND rq.status = 'Pending'
                    WHERE w.wikidata_qid IS NULL AND rq.id IS NULL AND w.curator_state IS NULL
                    """);
            }

            int activeIdentityJobs = await CountActiveIdentityJobsAsync(identityJobRepo, ct);
            sawExpectedAssetCount |= assetCount >= expectedCount;

            bool snapshotStable =
                assetCount == lastAssetCount
                && resolvedCount == lastResolvedCount
                && claimCount == lastClaimCount
                && pendingCount == lastPendingCount
                && activeIdentityJobs == lastActiveJobCount;

            stableSnapshots = snapshotStable ? stableSnapshots + 1 : 0;

            logger.LogInformation(
                "  Ingestion: assets={Assets}/{Expected}, resolved={Resolved}/{Works}, pending={Pending}, claims={Claims}, activeJobs={Jobs}, stable={Stable}/4",
                assetCount,
                expectedCount,
                resolvedCount,
                totalWorks,
                pendingCount,
                claimCount,
                activeIdentityJobs,
                stableSnapshots);

            if (assetCount >= expectedCount && totalWorks > 0 && pendingCount == 0 && activeIdentityJobs == 0 && stableSnapshots >= 2)
                return true;

            if (assetCount >= expectedCount && activeIdentityJobs == 0 && stableSnapshots >= 4)
                return true;

            if (sawExpectedAssetCount && activeIdentityJobs == 0 && stableSnapshots >= 6)
                return true;

            lastAssetCount = assetCount;
            lastResolvedCount = resolvedCount;
            lastClaimCount = claimCount;
            lastPendingCount = pendingCount;
            lastActiveJobCount = activeIdentityJobs;
        }

        return sawExpectedAssetCount || lastResolvedCount > 0;
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
                    FilePath = item.FilePath,
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
            ("Batman Year One", "comicvine", "Comics", MediaType.Comics, "comics"),
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
        IDatabaseConnection db,
        IRegistryRepository registryRepo,
        TestReport report,
        int stages,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var expectations = DevSeedEndpoints.GetAllExpectations()
            .GroupBy(e => NormalizeExpectationKey(e.Title, e.MediaType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerNamesById = await LoadProviderNamesByIdAsync(db);

        foreach (var item in allItems.Items)
        {
            // Fetch detail to get creator fields (author/director/artist)
            var detail = await registryRepo.GetDetailAsync(item.EntityId, ct);
            expectations.TryGetValue(NormalizeExpectationKey(item.Title, item.MediaType), out var expected);

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
                "Registered", "InReview", "Quarantined", "Rejected", "QidNoMatch",
            };

            bool creatorRequired =
                !item.MediaType.Equals("TV", StringComparison.OrdinalIgnoreCase)
                && (expected?.ExpectIdentified ?? true);
            string? expectedProvider = GetExpectedRetailProvider(expected, item.MediaType);
            string? actualProvider = detail is null
                ? null
                : ResolveRetailProvider(detail, providerNamesById);

            var check = new LibraryCheckResult
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
                ExpectedRetailProvider = expectedProvider,
                ActualRetailProvider = actualProvider,
                HasExpectedRetailProvider = string.IsNullOrWhiteSpace(expectedProvider)
                    || !string.Equals(item.RetailMatch, "matched", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(expectedProvider, actualProvider, StringComparison.OrdinalIgnoreCase),
            };
            report.LibraryChecks.Add(check);

            if (!check.HasTitle)
                report.IssuesFound.Add($"Library: '{item.FileName}' has no title");
            if (creatorRequired && !check.HasCreator)
                report.IssuesFound.Add($"Library: '{item.Title}' has no creator (author/director/artist)");
            if (!check.HasStatus)
                report.IssuesFound.Add($"Library: '{item.Title}' has invalid or empty status: '{item.Status}'");
            if (!check.HasRetailMatch)
                logger.LogWarning("  Library: '{Title}' missing retail match", item.Title);
            if (check.RequiresRetailProvider && !check.HasExpectedRetailProvider)
                report.IssuesFound.Add($"Library: '{item.Title}' retail provider '{actualProvider ?? "unknown"}' did not match expected '{expectedProvider}'");
            if (!check.HasCoverArt)
                logger.LogWarning("  Library: '{Title}' missing cover art", item.Title);
            if (stages >= 12 && !check.HasWikidataQid)
                logger.LogWarning("  Library: '{Title}' missing Wikidata QID (Stage 2 expected)", item.Title);
        }

        int passCount = report.LibraryChecks.Count(v => v.Pass);
        logger.LogInformation("  Library checks: {Pass}/{Total} items pass core validation",
            passCount, report.LibraryChecks.Count);

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
                var qidMethod = canonMap.GetValueOrDefault(MetadataFieldConstants.QidResolutionMethod);
                int childCount = CountChildEntities(hasChildren);
                bool requiresTrackManifest =
                    string.Equals(qidMethod, "album", StringComparison.OrdinalIgnoreCase)
                    || !string.IsNullOrWhiteSpace(hasChildren);

                if (!requiresTrackManifest)
                    continue;

                if (trackCount <= 0)
                    report.IssuesFound.Add($"Child entities: Music '{item.Title}' has no track_count");
                if (string.IsNullOrWhiteSpace(hasChildren))
                    report.IssuesFound.Add($"Child entities: Music '{item.Title}' has no child_entities_json");
                else if (childCount <= 0)
                    report.IssuesFound.Add($"Child entities: Music '{item.Title}' has invalid child_entities_json");
                else if (trackCount > 0 && childCount < trackCount)
                    report.IssuesFound.Add($"Child entities: Music '{item.Title}' exposes {childCount} child rows but track_count is {trackCount}");
                else
                    logger.LogInformation("  Child entities: Music '{Title}' — {Tracks} tracks",
                        item.Title, Math.Max(trackCount, childCount));
            }
        }
    }

    // ── Stage gating validation ──────────────────────────────────────────

    private static async Task ValidateFileSystemAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        IRegistryRepository registryRepo,
        TestReport report,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var organizer = new FileOrganizer(loggerFactory.CreateLogger<FileOrganizer>());
        var imagePathService = new ImagePathService(options.Value.LibraryRoot);
        var assetIds = await LoadWorkAssetIdsAsync(db);
        var assetCanonicals = await LoadCanonicalValueMapsAsync(db, assetIds.Values);
        var assetIdsByPath = await LoadAssetIdsByPathAsync(db);
        var watchRoots = ResolveLeafSourcePaths(configLoader);
        var expectations = DevSeedEndpoints.GetAllExpectations()
            .GroupBy(e => NormalizeExpectationKey(e.Title, e.MediaType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in allItems.Items)
        {
            var detail = await registryRepo.GetDetailAsync(item.EntityId, ct);
            expectations.TryGetValue(NormalizeExpectationKey(item.Title, item.MediaType), out var expected);

            bool expectLibraryPlacement = !IsReviewStatus(item.Status) && !IsFailureStatus(item.Status);
            bool expectedCoverArt = expected?.ExpectedCoverArt
                ?? (!string.IsNullOrWhiteSpace(item.CoverUrl) && expectLibraryPlacement);
            string? filePath = detail?.FilePath ?? item.FilePath;

            var check = new FileSystemCheckResult
            {
                Title = item.Title,
                MediaType = item.MediaType,
                Status = item.Status ?? "",
                ExpectedLocation = expectLibraryPlacement ? "Library" : "Staging",
                ExpectedCoverArt = expectedCoverArt,
            };

            Guid? resolvedAssetId = null;

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                check.ActualFilePath = filePath;
                check.ActualDisplayPath = ToDisplayPath(filePath, options.Value);
                check.FileExists = File.Exists(filePath);
                check.InLibraryRoot = IsUnderRoot(filePath, options.Value.LibraryRoot);
                check.InStagingRoot = IsUnderRoot(filePath, options.Value.StagingPath)
                    || watchRoots.Any(root => IsUnderRoot(filePath, root));
                check.LocationMatchesExpectation = expectLibraryPlacement
                    ? check.InLibraryRoot
                    : check.InStagingRoot;

                if (assetIdsByPath.TryGetValue(NormalizeComparablePath(filePath), out var assetIdByPath))
                    resolvedAssetId = assetIdByPath;
                else if (assetIds.TryGetValue(item.EntityId, out var assetIdByWork))
                    resolvedAssetId = assetIdByWork;

                if (expectLibraryPlacement
                    && resolvedAssetId is Guid pathAssetId
                    && assetCanonicals.TryGetValue(pathAssetId, out var pathMetadata)
                    && !string.IsNullOrWhiteSpace(options.Value.LibraryRoot))
                {
                    string? expectedPath = BuildExpectedLibraryPath(item, filePath, pathMetadata, options.Value, organizer);
                    check.RequiresTemplateMatch = !string.IsNullOrWhiteSpace(expectedPath);
                    check.PathMatchesTemplate = expectedPath is not null && PathsEqual(expectedPath, filePath);
                    if (!string.IsNullOrWhiteSpace(expectedPath))
                        check.ExpectedRelativePath = Path.GetRelativePath(options.Value.LibraryRoot, expectedPath);
                }
                else
                {
                    check.PathMatchesTemplate = !expectLibraryPlacement || check.InStagingRoot;
                }

                if (check.FileExists && expectLibraryPlacement && expectedCoverArt)
                {
                    check.RequiresSidecarArtwork = true;
                    check.HasPoster = HasAnySidecarPoster(filePath);
                    check.HasPosterThumb = HasAnySidecarPosterThumb(filePath);
                    check.HasHero = File.Exists(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, "hero.jpg"));
                }
            }

            resolvedAssetId ??= assetIds.TryGetValue(item.EntityId, out var fallbackAssetId) ? fallbackAssetId : null;
            if (resolvedAssetId is Guid assetId && !string.IsNullOrWhiteSpace(options.Value.LibraryRoot))
            {
                check.HasStoredCover = File.Exists(imagePathService.GetWorkCoverPath(item.WikidataQid, assetId));
                check.HasStoredCoverThumb = File.Exists(imagePathService.GetWorkCoverThumbPath(item.WikidataQid, assetId));
                check.HasStoredHero = File.Exists(imagePathService.GetWorkHeroPath(item.WikidataQid, assetId));
                check.HasStoredBackdrop = File.Exists(imagePathService.GetWorkBackdropPath(item.WikidataQid, assetId));
                check.HasStoredLogo = File.Exists(imagePathService.GetWorkLogoPath(item.WikidataQid, assetId));
                check.HasStoredBanner = File.Exists(imagePathService.GetWorkBannerPath(item.WikidataQid, assetId));
            }

            check.Detail = DescribeFileSystemCheck(check);
            report.FileSystemChecks.Add(check);
        }

        ValidateWatchFolders(configLoader, report, logger);

        int failedItems = report.FileSystemChecks.Count(c => !c.Pass);
        if (failedItems > 0)
        {
            report.IssuesFound.Add($"File system validation failed for {failedItems} item(s)");
            logger.LogWarning("  File system validation: {Failed}/{Total} items failed", failedItems, report.FileSystemChecks.Count);
        }

        int watchFailures = report.WatchFolderChecks.Count(c => !c.Pass);
        if (watchFailures > 0)
        {
            int remainingFiles = report.WatchFolderChecks.Sum(c => c.RemainingMediaFiles);
            report.IssuesFound.Add($"{remainingFiles} media file(s) remained in watch folders after ingestion");
            logger.LogWarning("  Watch folders not drained: {Directories} directories still contain {Files} media files",
                watchFailures, remainingFiles);
        }
    }

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
                // Only "matched" counts as a successful retail match. "failed" means
                // the provider ran but returned nothing; "none" means no provider ran
                // at all. Both should suppress the "QID expected" gating assertion.
                bool hasRetail = string.Equals(item.RetailMatch, "matched", StringComparison.OrdinalIgnoreCase);
                bool hasQid = !string.IsNullOrWhiteSpace(item.WikidataQid);
                bool retainedRetailWithoutQid = string.Equals(item.Status, "QidNoMatch", StringComparison.OrdinalIgnoreCase);

                if (hasRetail)
                {
                    var result = new StageGatingResult
                    {
                        Title  = item.Title,
                        Check  = "Stage 2: retail match → QID expected",
                        Pass   = hasQid || retainedRetailWithoutQid,
                        Detail = hasQid
                            ? $"QID: {item.WikidataQid}"
                            : retainedRetailWithoutQid
                                ? "Retail match retained without QID (accepted re-check state)"
                                : "Missing QID despite retail match",
                    };
                    report.StageGatingResults.Add(result);
                    if (!hasQid && !retainedRetailWithoutQid)
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

            // Poll for universe/parent collection creation (timeout: 3 minutes)
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
            int lastCollectionCount = 0;
            int stableCount = 0;
            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                int collectionCount;
                using (var conn = db.CreateConnection())
                    collectionCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM collections WHERE parent_collection_id IS NOT NULL;");

                if (collectionCount == lastCollectionCount && collectionCount > 0) stableCount++;
                else stableCount = 0;
                lastCollectionCount = collectionCount;

                logger.LogInformation("  Stage 3: {Collections} collections with parent, stable={Stable}", collectionCount, stableCount);
                if (stableCount >= 3) break;
            }

            // Validate universe creation for known test data
            using (var conn = db.CreateConnection())
            {
                // Check for parent collections (universes)
                var parentCollections = (await conn.QueryAsync<(string Id, string DisplayName, string? WikidataQid)>(
                    """
                    SELECT h.id, h.display_name, h.wikidata_qid
                    FROM collections h
                    WHERE EXISTS (SELECT 1 FROM collections child WHERE child.parent_collection_id = h.id)
                    """)).ToList();

                foreach (var ph in parentCollections)
                {
                    int childCount = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM collections WHERE parent_collection_id = @id;",
                        new { id = ph.Id });

                    var universeResult = new UniverseResult
                    {
                        Name        = ph.DisplayName,
                        WikidataQid = ph.WikidataQid,
                        Found       = true,
                        SeriesCount = childCount,
                    };
                    // Count works under child collections
                    int workCount = await conn.ExecuteScalarAsync<int>(
                        """
                        SELECT COUNT(*) FROM works w
                        INNER JOIN collections child ON w.collection_id = child.id
                        WHERE child.parent_collection_id = @id
                        """,
                        new { id = ph.Id });
                    universeResult.WorkCount = workCount;

                    report.UniverseResults.Add(universeResult);
                    logger.LogInformation("  Stage 3 Universe: '{Name}' — {Series} series, {Works} works, QID={Qid}",
                        ph.DisplayName, childCount, workCount, ph.WikidataQid ?? "none");
                }

                if (parentCollections.Count == 0)
                {
                    report.IssuesFound.Add("Stage 3: No parent collections (universes) created after enrichment");
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
        foreach (var check in report.LibraryChecks)
        {
            string k = $"{check.Title.ToLowerInvariant()}|{check.MediaType.ToLowerInvariant()}";
            // Prefer the "has cover art" result if any duplicate key already exists —
            // matches the "first resolved wins" semantics used for QID below.
            if (!coverArtByKey.TryGetValue(k, out var existing) || (check.HasCoverArt && !existing))
                coverArtByKey[k] = check.HasCoverArt;
        }

        var retailProviderByKey = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in report.LibraryChecks)
        {
            string k = $"{check.Title.ToLowerInvariant()}|{check.MediaType.ToLowerInvariant()}";
            if (!retailProviderByKey.ContainsKey(k) || !string.IsNullOrWhiteSpace(check.ActualRetailProvider))
                retailProviderByKey[k] = check.ActualRetailProvider;
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

                if (classification == "Match")
                {
                    string? expectedProvider = GetExpectedRetailProvider(exp, exp.MediaType);
                    string providerKey = $"{titleLower}|{mediaTypeLower}";
                    retailProviderByKey.TryGetValue(providerKey, out var actualProvider);
                    if (!string.IsNullOrWhiteSpace(expectedProvider)
                        && !string.IsNullOrWhiteSpace(actualProvider)
                        && !string.Equals(expectedProvider, actualProvider, StringComparison.OrdinalIgnoreCase))
                    {
                        classification = "WrongProvider";
                        actualDesc = $"Identified{(actual.WikidataQid is { Length: > 0 } q ? $" as {q}" : "")} via {actualProvider} (expected {expectedProvider})";
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
        SummaryCard(sb, report.LibraryChecks.Count(v => v.Pass).ToString() + "/" + report.LibraryChecks.Count, "Library Checks", "#22D3EE");
        SummaryCard(sb, report.FileSystemChecks.Count(f => f.Pass).ToString() + "/" + report.FileSystemChecks.Count, "Filesystem", "#38BDF8");
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
            ["TV"] = "#FBBF24", ["Music"] = "#22D3EE", ["Comics"] = "#7C4DFF",
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

        // Library Display Validation
        if (report.LibraryChecks.Count > 0)
        {
            int vaultPass = report.LibraryChecks.Count(v => v.Pass);
            string vaultBadge = vaultPass == report.LibraryChecks.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.LibraryChecks.Count - vaultPass} ISSUES</span>";
            sb.AppendLine($"<h2>Library Display Validation {vaultBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Cover Art</th><th>Title</th><th>Creator</th><th>Status</th><th>Retail Match</th><th>Retail Provider</th><th>QID</th></tr>");
            foreach (var v in report.LibraryChecks.OrderBy(v => v.MediaType).ThenBy(v => v.Title))
            {
                string Check(bool ok) => ok ? "<span style=\"color:#5DCAA5\">&#x2713;</span>" : "<span style=\"color:#E24B4A\">&#x2717;</span>";
                sb.AppendLine($"<tr><td>{Esc(v.Title)}</td><td>{Esc(v.MediaType)}</td>" +
                    $"<td>{Check(v.HasCoverArt)}</td><td>{Check(v.HasTitle)}</td><td>{Check(v.HasCreator)}</td>" +
                    $"<td>{Check(v.HasStatus)} <span class=\"mono\">{Esc(v.Status ?? "")}</span></td>" +
                    $"<td>{Check(v.HasRetailMatch)} <span class=\"mono\">{Esc(v.RetailMatch ?? "")}</span></td>" +
                    $"<td>{Check(v.HasExpectedRetailProvider)} <span class=\"mono\">{Esc(v.ActualRetailProvider ?? "â€”")}</span></td>" +
                    $"<td>{Check(v.HasWikidataQid)} <span class=\"mono\">{Esc(v.WikidataQid ?? "")}</span></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        // Filesystem Validation
        if (report.FileSystemChecks.Count > 0)
        {
            int fsPass = report.FileSystemChecks.Count(f => f.Pass);
            string fsBadge = fsPass == report.FileSystemChecks.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.FileSystemChecks.Count - fsPass} ISSUES</span>";
            sb.AppendLine($"<h2>Filesystem Validation {fsBadge}</h2>");

            if (report.WatchFolderChecks.Count > 0)
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Watch Folder</th><th>Unexpected Media Files</th><th>Ignored Expected Staging</th><th>Status</th></tr>");
                foreach (var check in report.WatchFolderChecks.OrderBy(c => c.Directory))
                {
                    string status = check.Pass
                        ? "<span class=\"badge badge-pass\">EMPTY</span>"
                        : "<span class=\"badge badge-fail\">REMAINING FILES</span>";
                    sb.AppendLine($"<tr><td class=\"mono\">{Esc(check.Directory)}</td><td>{check.RemainingMediaFiles}</td><td>{check.IgnoredExpectedStagingFiles}</td><td>{status}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            var failingChecks = report.FileSystemChecks.Where(f => !f.Pass).OrderBy(f => f.MediaType).ThenBy(f => f.Title).ToList();
            if (failingChecks.Count > 0)
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Status</th><th>Expected</th><th>Actual</th><th>Sidecars</th><th>Stored Art</th><th>Detail</th></tr>");
                foreach (var check in failingChecks)
                {
                    string sidecars = check.RequiresSidecarArtwork
                        ? $"{BoolMark(check.HasPoster)}/{BoolMark(check.HasPosterThumb)}/{BoolMark(check.HasHero)}"
                        : "n/a";
                string stored = check.RequiresStoredArtwork
                    ? $"{BoolMark(check.HasStoredCover)}/{BoolMark(check.HasStoredCoverThumb)}/{BoolMark(check.HasStoredHero)}"
                    : "n/a";

                    sb.AppendLine($"<tr><td>{Esc(check.Title)}</td><td>{Esc(check.MediaType)}</td><td>{Esc(check.Status)}</td>" +
                        $"<td class=\"mono\">{Esc(check.ExpectedLocation)}{(!string.IsNullOrWhiteSpace(check.ExpectedRelativePath) ? "<br>" + Esc(check.ExpectedRelativePath) : "")}</td>" +
                        $"<td class=\"mono\">{Esc(check.ActualDisplayPath ?? check.ActualFilePath ?? "—")}</td>" +
                        $"<td class=\"mono\">{Esc(sidecars)}</td><td class=\"mono\">{Esc(stored)}</td><td>{Esc(check.Detail)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine($"<details{(failingChecks.Count == 0 ? " open" : "")}><summary style=\"cursor:pointer;color:#8B9DC3;font-weight:600\">All filesystem checks ({report.FileSystemChecks.Count})</summary>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Result</th><th>Expected</th><th>Actual</th><th>Template</th><th>Sidecars</th><th>Stored Core</th><th>Optional Stored Art</th></tr>");
            foreach (var check in report.FileSystemChecks.OrderBy(f => f.MediaType).ThenBy(f => f.Title))
            {
                string result = check.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                string optionalArt = $"{BoolMark(check.HasStoredBackdrop)}/{BoolMark(check.HasStoredLogo)}/{BoolMark(check.HasStoredBanner)}";
                string sidecars = check.RequiresSidecarArtwork
                    ? $"{BoolMark(check.HasPoster)}/{BoolMark(check.HasPosterThumb)}/{BoolMark(check.HasHero)}"
                    : "n/a";
                string storedCore = check.RequiresStoredArtwork
                    ? $"{BoolMark(check.HasStoredCover)}/{BoolMark(check.HasStoredCoverThumb)}/{BoolMark(check.HasStoredHero)}"
                    : "n/a";
                string templateState = check.RequiresTemplateMatch
                    ? BoolMark(check.PathMatchesTemplate)
                    : "n/a";

                sb.AppendLine($"<tr><td>{Esc(check.Title)}</td><td>{Esc(check.MediaType)}</td><td>{result}</td>" +
                    $"<td class=\"mono\">{Esc(check.ExpectedLocation)}{(!string.IsNullOrWhiteSpace(check.ExpectedRelativePath) ? "<br>" + Esc(check.ExpectedRelativePath) : "")}</td>" +
                    $"<td class=\"mono\">{Esc(check.ActualDisplayPath ?? check.ActualFilePath ?? "—")}</td>" +
                    $"<td class=\"mono\">{Esc(templateState)}</td><td class=\"mono\">{Esc(sidecars)}</td><td class=\"mono\">{Esc(storedCore)}</td><td class=\"mono\">{Esc(optionalArt)}</td></tr>");
            }
            sb.AppendLine("</table>");
            sb.AppendLine("</details>");
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
            var classOrder = new[] { "UnexpectedReview", "UnexpectedIdentified", "WrongTrigger", "WrongQid", "WrongProvider", "MissingCoverArt", "NotFound" };
            var classLabels = new Dictionary<string, string>
            {
                ["UnexpectedReview"]    = "Unexpected Review (expected Identified, got InReview)",
                ["UnexpectedIdentified"] = "Unexpected Identified (expected InReview, got Identified)",
                ["WrongTrigger"]        = "Wrong Trigger (in review, but wrong trigger)",
                ["WrongQid"]            = "Wrong Wikidata QID (resolved, but to the wrong entity)",
                ["WrongProvider"]       = "Wrong Retail Provider (matched, but from an unexpected provider)",
                ["MissingCoverArt"]     = "Missing Cover Art (identified, but no cover downloaded)",
                ["NotFound"]            = "Not Found (no Work row in database)",
            };
            var classColors = new Dictionary<string, string>
            {
                ["UnexpectedReview"]    = "#E24B4A",
                ["UnexpectedIdentified"] = "#E24B4A",
                ["WrongTrigger"]        = "#EF9F27",
                ["WrongQid"]            = "#E24B4A",
                ["WrongProvider"]       = "#EF9F27",
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

    private static string BoolMark(bool value) => value ? "Y" : "N";

    private static string StatusClass(string? status)
    {
        var s = (status ?? "").ToUpperInvariant();
        if (s.Contains("IDENTIFIED") || s.Contains("CONFIRMED") || s.Contains("REGISTERED")) return "status-identified";
        if (s.Contains("REVIEW")) return "status-review";
        if (s.Contains("FAIL") || s.Contains("QUARANTINE")) return "status-failed";
        return "status-unknown";
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static Task<int> CountActiveIdentityJobsAsync(
        IIdentityJobRepository identityJobRepo,
        CancellationToken ct)
        => identityJobRepo.CountActiveAsync(ct);

    private static async Task<Dictionary<Guid, Guid>> LoadWorkAssetIdsAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string WorkId, string AssetId)>("""
            SELECT w.id AS WorkId, MIN(ma.id) AS AssetId
            FROM works w
            INNER JOIN editions e ON e.work_id = w.id
            INNER JOIN media_assets ma ON ma.edition_id = e.id
            GROUP BY w.id
            """);

        return rows
            .Where(r => Guid.TryParse(r.WorkId, out _) && Guid.TryParse(r.AssetId, out _))
            .ToDictionary(r => Guid.Parse(r.WorkId), r => Guid.Parse(r.AssetId));
    }

    private static async Task<Dictionary<string, Guid>> LoadAssetIdsByPathAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string AssetId, string FilePath)>("""
            SELECT id             AS AssetId,
                   file_path_root AS FilePath
            FROM media_assets
            WHERE file_path_root IS NOT NULL
              AND TRIM(file_path_root) <> '';
            """);

        return rows
            .Where(r => Guid.TryParse(r.AssetId, out _)
                && !string.IsNullOrWhiteSpace(r.FilePath))
            .GroupBy(r => NormalizeComparablePath(r.FilePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => Guid.Parse(g.OrderBy(r => r.AssetId, StringComparer.OrdinalIgnoreCase).First().AssetId),
                StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<Dictionary<Guid, Dictionary<string, string>>> LoadCanonicalValueMapsAsync(
        IDatabaseConnection db,
        IEnumerable<Guid> entityIds)
    {
        var ids = entityIds.Distinct().ToList();
        if (ids.Count == 0)
            return [];

        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string EntityId, string Key, string Value)>("""
            SELECT entity_id AS EntityId,
                   key       AS Key,
                   value     AS Value
            FROM canonical_values
            WHERE entity_id IN @entityIds
              AND value IS NOT NULL
              AND TRIM(value) <> '';
            """, new
        {
            entityIds = ids.Select(id => id.ToString()).ToArray(),
        });

        var maps = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var row in rows)
        {
            if (!Guid.TryParse(row.EntityId, out var entityId))
                continue;

            if (!maps.TryGetValue(entityId, out var map))
            {
                map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                maps[entityId] = map;
            }

            map[row.Key] = row.Value;
        }

        return maps;
    }

    private static string NormalizeExpectationKey(string title, string mediaType) =>
        $"{title.Trim().ToLowerInvariant()}|{mediaType.Trim().ToLowerInvariant()}";

    private sealed class ProviderNameRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> LoadProviderNamesByIdAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<ProviderNameRow>(
            "SELECT id AS Id, name AS Name FROM provider_registry");

        var providerNames = new Dictionary<Guid, string>();
        foreach (var row in rows)
        {
            if (!Guid.TryParse(row.Id, out var providerId))
                continue;

            var normalized = NormalizeProviderName(row.Name);
            if (string.IsNullOrWhiteSpace(normalized))
                continue;

            providerNames[providerId] = normalized;
        }

        return providerNames;
    }

    private static string? GetExpectedRetailProvider(DevSeedEndpoints.SeedExpectation? expected, string mediaType)
    {
        var provider = expected?.ExpectedProvider ?? InferExpectedRetailProvider(mediaType);
        return NormalizeProviderName(provider);
    }

    private static string? InferExpectedRetailProvider(string mediaType) =>
        mediaType.Trim().ToLowerInvariant() switch
        {
            "audiobooks" or "music" => "apple_api",
            "movies" or "tv" => "tmdb",
            "comics" => "comicvine",
            _ => null,
        };

    private static string? ResolveRetailProvider(
        RegistryItemDetail detail,
        IReadOnlyDictionary<Guid, string> providerNamesById)
    {
        var provider = detail.ClaimHistory
            .Where(c => c.ProviderId != Guid.Empty
                        && providerNamesById.TryGetValue(c.ProviderId, out _)
                        && IsRetailProviderClaim(c.ClaimKey))
            .OrderByDescending(c => c.Confidence)
            .ThenByDescending(c => c.ClaimedAt)
            .Select(c => providerNamesById[c.ProviderId])
            .FirstOrDefault(p => !IsNonRetailProvider(p));

        if (!string.IsNullOrWhiteSpace(provider))
            return provider;

        return NormalizeProviderName(detail.MatchSource);
    }

    private static bool IsRetailProviderClaim(string claimKey) =>
        claimKey is MetadataFieldConstants.Title
            or MetadataFieldConstants.Album
            or MetadataFieldConstants.ShowName
            or MetadataFieldConstants.EpisodeTitle
            or MetadataFieldConstants.Series;

    private static bool IsNonRetailProvider(string? provider) =>
        string.IsNullOrWhiteSpace(provider)
        || provider.StartsWith("local", StringComparison.OrdinalIgnoreCase)
        || provider.Contains("wikidata", StringComparison.OrdinalIgnoreCase)
        || provider.Contains("libraryscan", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeProviderName(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        Span<char> buffer = stackalloc char[providerName.Length];
        int index = 0;
        bool previousUnderscore = false;

        foreach (var ch in providerName.Trim().ToLowerInvariant())
        {
            var normalized = char.IsLetterOrDigit(ch) ? ch : '_';
            if (normalized == '_')
            {
                if (previousUnderscore)
                    continue;
                previousUnderscore = true;
            }
            else
            {
                previousUnderscore = false;
            }

            buffer[index++] = normalized;
        }

        var result = new string(buffer[..index]).Trim('_');
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static int CountChildEntities(string? childEntitiesJson)
    {
        if (string.IsNullOrWhiteSpace(childEntitiesJson))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(childEntitiesJson);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement.GetArrayLength(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("items", out var items)
                    && items.ValueKind == JsonValueKind.Array => items.GetArrayLength(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("tracks", out var tracks)
                    && tracks.ValueKind == JsonValueKind.Array => tracks.GetArrayLength(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("episodes", out var episodes)
                    && episodes.ValueKind == JsonValueKind.Array => episodes.GetArrayLength(),
                JsonValueKind.Object when doc.RootElement.TryGetProperty("issues", out var issues)
                    && issues.ValueKind == JsonValueKind.Array => issues.GetArrayLength(),
                _ => 0,
            };
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsReviewStatus(string? status) =>
        (status ?? "").Contains("review", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureStatus(string? status)
    {
        var value = status ?? "";
        return value.Contains("fail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("quarantine", StringComparison.OrdinalIgnoreCase)
            || value.Contains("reject", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderRoot(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root))
            return false;

        string normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToDisplayPath(string path, IngestionOptions options)
    {
        if (IsUnderRoot(path, options.LibraryRoot))
            return $"library/{Path.GetRelativePath(options.LibraryRoot, path).Replace('\\', '/')}";
        if (IsUnderRoot(path, options.StagingPath))
            return $"staging/{Path.GetRelativePath(options.StagingPath, path).Replace('\\', '/')}";

        return path;
    }

    private static string? BuildExpectedLibraryPath(
        RegistryItem item,
        string? filePath,
        IReadOnlyDictionary<string, string> metadata,
        IngestionOptions options,
        FileOrganizer organizer)
    {
        if (string.IsNullOrWhiteSpace(options.LibraryRoot) || string.IsNullOrWhiteSpace(filePath))
            return null;

        MediaType? mediaType = Enum.TryParse<MediaType>(item.MediaType, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        var candidate = new IngestionCandidate
        {
            Path = filePath,
            EventType = FileEventType.Created,
            DetectedAt = DateTimeOffset.UtcNow,
            ReadyAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
            DetectedMediaType = mediaType,
        };

        string template = options.ResolveTemplate(mediaType?.ToString() ?? item.MediaType);
        string relative = organizer.CalculatePath(candidate, template).Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(options.LibraryRoot, relative);
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static string DescribeFileSystemCheck(FileSystemCheckResult check)
    {
        if (!check.FileExists)
            return "File path is missing on disk";
        if (!check.LocationMatchesExpectation)
            return check.ExpectedLocation == "Library"
                ? "File never reached the organized library"
                : "File should still be in staging";
        if (check.RequiresTemplateMatch && !check.PathMatchesTemplate)
            return $"Template drift: expected {check.ExpectedRelativePath}";
        if (check.RequiresSidecarArtwork && (!check.HasPoster || !check.HasPosterThumb || !check.HasHero))
            return "Sidecar artwork is incomplete next to the media file";
        if (check.RequiresStoredArtwork && (!check.HasStoredCover || !check.HasStoredCoverThumb || !check.HasStoredHero))
            return "Stored artwork set is incomplete under .data/images";

        return "OK";
    }

    private static void ValidateWatchFolders(
        IConfigurationLoader configLoader,
        TestReport report,
        ILogger logger)
    {
        var expectedStagingPaths = report.FileSystemChecks
            .Where(c => c.ExpectedLocation == "Staging"
                && c.FileExists
                && !string.IsNullOrWhiteSpace(c.ActualFilePath))
            .Select(c => NormalizeComparablePath(c.ActualFilePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string path in ResolveLeafSourcePaths(configLoader))
        {
            report.WatchFolderChecks.Add(new WatchFolderCheckResult
            {
                Directory = path,
                RemainingMediaFiles = CountMediaFiles(path, expectedStagingPaths),
                IgnoredExpectedStagingFiles = CountIgnoredMediaFiles(path, expectedStagingPaths),
            });
        }

        logger.LogInformation("  Watch folders: {Empty}/{Total} drained",
            report.WatchFolderChecks.Count(c => c.Pass),
            report.WatchFolderChecks.Count);
    }

    private static IReadOnlyList<string> ResolveLeafSourcePaths(IConfigurationLoader configLoader)
    {
        var allPaths = configLoader.LoadLibraries().Libraries
            .SelectMany(lib =>
                (lib.SourcePaths is { Count: > 0 } paths ? paths : [lib.SourcePath])
                    .Where(p => !string.IsNullOrWhiteSpace(p)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToList();

        return allPaths
            .Where(path => !allPaths.Any(other =>
                !path.Equals(other, StringComparison.OrdinalIgnoreCase)
                && other.StartsWith(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(path => path)
            .ToList();
    }

    private static int CountMediaFiles(string directory, ISet<string>? ignoredPaths = null)
    {
        if (!Directory.Exists(directory))
            return 0;

        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Count(path =>
                    MediaExtensions.Contains(Path.GetExtension(path))
                    && (ignoredPaths is null || !ignoredPaths.Contains(NormalizeComparablePath(path))));
        }
        catch
        {
            return 0;
        }
    }

    private static int CountIgnoredMediaFiles(string directory, ISet<string> ignoredPaths)
    {
        if (!Directory.Exists(directory) || ignoredPaths.Count == 0)
            return 0;

        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Count(path =>
                    MediaExtensions.Contains(Path.GetExtension(path))
                    && ignoredPaths.Contains(NormalizeComparablePath(path)));
        }
        catch
        {
            return 0;
        }
    }

    private static bool HasAnySidecarPoster(string mediaFilePath) =>
        EnumeratePosterCandidates(mediaFilePath).Any(File.Exists);

    private static bool HasAnySidecarPosterThumb(string mediaFilePath) =>
        EnumeratePosterThumbCandidates(mediaFilePath).Any(File.Exists);

    private static IEnumerable<string> EnumeratePosterCandidates(string mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
            yield break;

        var dir = Path.GetDirectoryName(mediaFilePath) ?? string.Empty;
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[]
        {
            ImagePathService.GetMediaFilePosterPath(mediaFilePath),
            Path.Combine(dir, "poster.jpg"),
            Path.Combine(dir, $"{basename}-poster.jpg"),
        })
        {
            if (yielded.Add(candidate))
                yield return candidate;
        }
    }

    private static IEnumerable<string> EnumeratePosterThumbCandidates(string mediaFilePath)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath))
            yield break;

        var dir = Path.GetDirectoryName(mediaFilePath) ?? string.Empty;
        var basename = Path.GetFileNameWithoutExtension(mediaFilePath);
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in new[]
        {
            ImagePathService.GetMediaFileThumbPath(mediaFilePath),
            Path.Combine(dir, "poster-thumb.jpg"),
            Path.Combine(dir, $"{basename}-poster-thumb.jpg"),
        })
        {
            if (yielded.Add(candidate))
                yield return candidate;
        }
    }

    private static string NormalizeComparablePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
