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
    // ── Test case definitions (expected outcomes) ──────────────────────────

    private sealed record TestExpectation(
        string Title,
        string ExpectedMediaType,
        string ExpectedProvider,
        string SearchQuery,
        bool ExpectIdentified);

    private static readonly TestExpectation[] BookExpectations =
    [
        new("Dune", "Books", "apple_api", "Dune Frank Herbert", true),
        new("Project Hail Mary", "Books", "apple_api", "Project Hail Mary Andy Weir", true),
        new("The Hobbit", "Books", "apple_api", "The Hobbit Tolkien", true),
        new("Harry Potter and the Philosopher's Stone", "Books", "apple_api", "Harry Potter Philosopher's Stone", true),
    ];

    private static readonly TestExpectation[] AudiobookExpectations =
    [
        new("Dune", "Audiobooks", "apple_api", "Dune Frank Herbert audiobook", true),
        new("Project Hail Mary", "Audiobooks", "apple_api", "Project Hail Mary audiobook", true),
    ];

    private static readonly TestExpectation[] MovieExpectations =
    [
        new("Blade Runner 2049", "Movies", "tmdb", "Blade Runner 2049", true),
        new("The Matrix", "Movies", "tmdb", "The Matrix", true),
        new("Interstellar", "Movies", "tmdb", "Interstellar", true),
        new("Spirited Away", "Movies", "tmdb", "Spirited Away", true),
    ];

    private static readonly TestExpectation[] TvExpectations =
    [
        new("Breaking Bad", "TV", "tmdb", "Breaking Bad", true),
        new("The Expanse", "TV", "tmdb", "The Expanse", true),
    ];

    private static readonly TestExpectation[] MusicExpectations =
    [
        new("Bohemian Rhapsody", "Music", "musicbrainz", "Bohemian Rhapsody Queen", true),
        new("Clair de Lune", "Music", "musicbrainz", "Clair de Lune Debussy", true),
    ];

    // ── Test result models ────────────────────────────────────────────────

    private sealed class TestReport
    {
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public TimeSpan TotalDuration { get; set; }
        public string WipeStatus { get; set; } = "";
        public string SeedStatus { get; set; } = "";
        public int TotalFilesSeeded { get; set; }
        public TimeSpan IngestionDuration { get; set; }
        public List<MediaTypeResult> MediaTypeResults { get; set; } = [];
        public List<ManualSearchResult> ManualSearchResults { get; set; } = [];
        public List<UniverseResult> UniverseResults { get; set; } = [];
        public List<string> IssuesFound { get; set; } = [];
        public List<string> FixesApplied { get; set; } = [];
        public int TotalItems { get; set; }
        public int TotalIdentified { get; set; }
        public int TotalNeedsReview { get; set; }
        public int TotalFailed { get; set; }
        public bool OverallPass => IssuesFound.Count == 0;
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

        logger.LogInformation("╔══════════════════════════════════════════╗");
        logger.LogInformation("║   INTEGRATION TEST — Starting            ║");
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

        // ── Phase 2: Seed (no comics, no podcasts) ────────────────────────
        logger.LogInformation("[Phase 2] Seeding test files (excluding comics and podcasts)...");
        try
        {
            int seeded = await SeedInternalAsync(options, configLoader, logger);
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
        bool ingestionComplete = await WaitForIngestionAsync(db, logger, TimeSpan.FromMinutes(8), ct);
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

        // ── Phase 5: Test manual search for review items ──────────────────
        logger.LogInformation("[Phase 5] Testing manual search on review items...");
        await TestManualSearchAsync(registryRepo, providers, report, logger, ct);

        // ── Phase 6: Check universe enrichment ────────────────────────────
        logger.LogInformation("[Phase 6] Checking universe enrichment...");
        await CheckUniversesAsync(registryRepo, report, logger, ct);

        sw.Stop();
        report.TotalDuration = sw.Elapsed;

        // ── Generate HTML report ──────────────────────────────────────────
        string html = GenerateHtmlReport(report);

        // Save to disk
        string reportsDir = Path.Combine(
            Path.GetDirectoryName(typeof(IntegrationTestEndpoints).Assembly.Location) ?? ".",
            "..", "..", "..", "..", "..", "tools", "reports");
        try
        {
            if (!Directory.Exists(reportsDir))
                reportsDir = Path.Combine(Directory.GetCurrentDirectory(), "tools", "reports");
            if (!Directory.Exists(reportsDir))
                Directory.CreateDirectory(reportsDir);

            string fileName = $"integration-test-{DateTime.Now:yyyy-MM-dd-HHmmss}.html";
            string filePath = Path.Combine(reportsDir, fileName);
            await File.WriteAllTextAsync(filePath, html, ct);
            logger.LogInformation("[Report] HTML report saved to {Path}", filePath);
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
                      AND name NOT IN ('schema_version','provider_registry','provider_config','provider_health','profiles','api_keys')
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

    // ── Internal seed (no comics, no podcasts) ────────────────────────────

    private static async Task<int> SeedInternalAsync(
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        ILogger logger)
    {
        var libConfig = configLoader.LoadLibraries();
        int total = 0;

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
            foreach (var book in DevSeedEndpoints_SeedBooks())
            {
                string fileName = $"{SanitizeFileName(book.Title)}.epub";
                string filePath = Path.Combine(booksDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] epub = EpubBuilder.Create(book.Title, book.Author, book.Isbn, book.Year, book.Description,
                    book.Publisher, book.Language, book.AdditionalAuthors, book.Series, book.SeriesPosition);
                await File.WriteAllBytesAsync(filePath, epub);
                total++;
            }

            // Audiobooks in dedicated subfolder for unambiguous classification
            var audiobooksDir = ResolveDir("Audiobooks");
            // Fall back to an audiobooks subfolder under books if no dedicated library entry
            if (string.IsNullOrWhiteSpace(audiobooksDir) || audiobooksDir == booksDir)
                audiobooksDir = Path.Combine(Path.GetDirectoryName(booksDir)!, "audiobooks");
            EnsureDir(audiobooksDir);
            foreach (var ab in DevSeedEndpoints_SeedAudiobooks())
            {
                string fileName = $"{SanitizeFileName(ab.Title)} - {SanitizeFileName(ab.Narrator)}.mp3";
                string filePath = Path.Combine(audiobooksDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] mp3 = Mp3Builder.Create(ab.Title, ab.Artist, narrator: ab.Narrator,
                    year: ab.Year, language: ab.Language, series: ab.Series, seriesPosition: ab.SeriesPosition, asin: ab.Asin);
                await File.WriteAllBytesAsync(filePath, mp3);
                total++;
            }
        }

        // Movies
        var moviesDir = ResolveDir("Movies");
        if (!string.IsNullOrWhiteSpace(moviesDir))
        {
            EnsureDir(moviesDir);
            foreach (var v in DevSeedEndpoints_SeedVideos().Where(v => v.MediaType == "Movie"))
            {
                string fileName = $"{SanitizeFileName(v.Title)} ({v.Year}).mp4";
                string filePath = Path.Combine(moviesDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] mp4 = Mp4Builder.Create(v.Title, v.Director, v.Year);
                await File.WriteAllBytesAsync(filePath, mp4);
                total++;
            }
        }

        // TV
        var tvDir = ResolveDir("TV");
        if (!string.IsNullOrWhiteSpace(tvDir))
        {
            EnsureDir(tvDir);
            foreach (var v in DevSeedEndpoints_SeedVideos().Where(v => v.MediaType == "TV"))
            {
                string fileName = v.SeasonNumber is not null && v.EpisodeNumber is not null
                    ? $"{SanitizeFileName(v.Series ?? v.Title)} S{v.SeasonNumber:D2}E{v.EpisodeNumber:D2}.mp4"
                    : $"{SanitizeFileName(v.Title)} ({v.Year}).mp4";
                string filePath = Path.Combine(tvDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] mp4 = Mp4Builder.Create(v.Title, v.Director, v.Year);
                await File.WriteAllBytesAsync(filePath, mp4);
                total++;
            }
        }

        // Music
        var musicDir = ResolveDir("Music");
        if (!string.IsNullOrWhiteSpace(musicDir))
        {
            EnsureDir(musicDir);
            foreach (var m in DevSeedEndpoints_SeedMusic())
            {
                string fileName = $"{SanitizeFileName(m.Artist)} - {SanitizeFileName(m.Title)}.flac";
                string filePath = Path.Combine(musicDir, fileName);
                if (File.Exists(filePath)) continue;
                byte[] flac = FlacBuilder.Create(m.Title, m.Artist, m.Album, m.Year, m.Genre, m.TrackNumber);
                await File.WriteAllBytesAsync(filePath, flac);
                total++;
            }
        }

        logger.LogInformation("[Seed] {Count} test files created (no comics, no podcasts)", total);
        return total;
    }

    // ── Wait for ingestion ────────────────────────────────────────────────

    private static async Task<bool> WaitForIngestionAsync(
        IDatabaseConnection db,
        ILogger logger,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        // Phase 1: Wait for asset ingestion to stabilize.
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

            logger.LogInformation("  Ingestion: {Count} assets, stable={Stable}", assetCount, assetStable);
            if (assetCount > 0 && assetStable >= 3) break;
        }

        // Phase 2: Wait for hydration (review queue + QID resolution) to stabilize.
        // This tracks the number of items visible in the registry (items with QID or review entries).
        int lastVisibleCount = 0;
        int hydrationStable = 0;
        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct);
            int visibleCount;
            using (var conn = db.CreateConnection())
            {
                visibleCount = conn.ExecuteScalar<int>("""
                    SELECT COUNT(DISTINCT w.id) FROM works w
                    LEFT JOIN editions e ON e.work_id = w.id
                    LEFT JOIN media_assets ma ON ma.edition_id = e.id
                    LEFT JOIN review_queue rq ON rq.entity_id = ma.id AND rq.status = 'Pending'
                    WHERE w.wikidata_qid IS NOT NULL OR rq.id IS NOT NULL OR w.curator_state IS NOT NULL
                    """);
            }

            if (visibleCount == lastVisibleCount && visibleCount > 0) hydrationStable++;
            else hydrationStable = 0;
            lastVisibleCount = visibleCount;

            logger.LogInformation("  Hydration: {Visible}/{Total} visible in registry, stable={Stable}",
                visibleCount, lastAssetCount, hydrationStable);
            if (visibleCount > 0 && hydrationStable >= 4) return true;
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
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500), ct);
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
        foreach (var expected in new[] { "Books", "Audiobooks", "Movies", "TV", "Music" })
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
        var searchTests = new (string query, string providerName, string mediaType, MediaType enumType)[]
        {
            ("Dune Frank Herbert", "apple_api", "Books", MediaType.Books),
            ("Blade Runner 2049", "tmdb", "Movies", MediaType.Movies),
            ("Breaking Bad", "tmdb", "TV", MediaType.TV),
            ("Bohemian Rhapsody Queen", "musicbrainz", "Music", MediaType.Music),
        };

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

    // ── Check universes ───────────────────────────────────────────────────

    private static async Task CheckUniversesAsync(
        IRegistryRepository registryRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Check if items that should form universes got QIDs (prerequisite for universe formation)
        var allItems = await registryRepo.GetPageAsync(new RegistryQuery(Offset: 0, Limit: 500), ct);

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
        sb.AppendLine($"<p class=\"subtitle\">{report.Timestamp:yyyy-MM-dd HH:mm:ss UTC} · Duration: {report.TotalDuration.TotalSeconds:F1}s · Ingestion: {report.IngestionDuration.TotalSeconds:F1}s</p>");

        // Summary cards
        sb.AppendLine("<div class=\"summary-grid\">");
        SummaryCard(sb, report.TotalFilesSeeded.ToString(), "Files Seeded", "#60A5FA");
        SummaryCard(sb, report.TotalItems.ToString(), "Items Detected", "#8B9DC3");
        SummaryCard(sb, report.TotalIdentified.ToString(), "Identified", "#5DCAA5");
        SummaryCard(sb, report.TotalNeedsReview.ToString(), "Needs Review", "#EF9F27");
        SummaryCard(sb, report.TotalFailed.ToString(), "Failed", "#E24B4A");
        SummaryCard(sb, report.ManualSearchResults.Count(s => s.Pass).ToString() + "/" + report.ManualSearchResults.Count, "Search Tests", "#A78BFA");
        sb.AppendLine("</div>");

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
        new("Lose Yourself", "Eminem", Album: "8 Mile Soundtrack", Year: 2002, Genre: "Hip-Hop", TrackNumber: 1),
        new("Across the Stars", "John Williams", Album: "Star Wars: Attack of the Clones", Year: 2002, Genre: "Soundtrack", TrackNumber: 3),
        new("Nuvole Bianche", "Ludovico Einaudi", Album: "Una Mattina", Year: 2004, Genre: "Classical", TrackNumber: 6),
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
