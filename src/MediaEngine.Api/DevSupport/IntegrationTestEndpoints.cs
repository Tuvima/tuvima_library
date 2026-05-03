using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Dapper;
using MediaEngine.Api.Endpoints;
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
///   POST /dev/integration-test  â€” Full cycle: wipe â†’ seed â†’ ingest â†’ validate â†’ report (HTML)
/// </summary>
public static class IntegrationTestEndpoints
{
    // â”€â”€ Test case definitions â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    //
    // Expectations are now read at runtime from DevSeedEndpoints.GetAllExpectations()
    // so the seed records themselves are the single source of truth. The previous
    // hardcoded TestExpectation[] arrays drifted out of sync with the seed list and
    // were never actually consulted by the reconciliation pass.

    // â”€â”€ Test result models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
        public List<Stage3FanartSummary> Stage3FanartSummaries { get; set; } = [];
        public List<CharacterArtworkCheckResult> CharacterArtworkChecks { get; set; } = [];
        public List<DescriptionSourceCheckResult> DescriptionSourceChecks { get; set; } = [];
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
        public bool RequiresCreator { get; set; } = true;
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
        public string? WikidataQid { get; set; }
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
        public bool HasLegacyHeroSidecar { get; set; }
        public bool RequiresStoredArtwork { get; set; }
        public bool HasStoredCover { get; set; }
        public bool HasStoredCoverSmall { get; set; }
        public bool HasStoredCoverMedium { get; set; }
        public bool HasStoredCoverLarge { get; set; }
        public bool HasStoredPalette { get; set; }
        public bool HasStoredLegacyHero { get; set; }
        public bool HasStoredBackground { get; set; }
        public bool HasStoredLogo { get; set; }
        public bool HasStoredBanner { get; set; }
        public bool HasStoredDiscArt { get; set; }
        public bool HasStoredClearArt { get; set; }
        public bool HasStoredSeasonPoster { get; set; }
        public bool HasStoredSeasonThumb { get; set; }
        public bool HasStoredEpisodeStill { get; set; }
        public bool HasFanartBridgeId { get; set; }
        public string? Detail { get; set; }
        public bool Pass =>
            FileExists
            && LocationMatchesExpectation
            && (!RequiresTemplateMatch || PathMatchesTemplate)
            && (!RequiresSidecarArtwork || (HasPoster && HasPosterThumb))
            && (!RequiresStoredArtwork || (HasStoredCover && HasStoredCoverSmall && HasStoredCoverMedium && HasStoredCoverLarge && HasStoredPalette))
            && !HasLegacyHeroSidecar
            && !HasStoredLegacyHero;
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

    // â”€â”€ Reconciliation models â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Type-level evidence that Stage 3 fanart assets were stored.</summary>
    private sealed class Stage3FanartSummary
    {
        public string MediaType { get; set; } = "";
        public int EligibleCount { get; set; }
        public int WithAnyFanart { get; set; }
        public int WithBackground { get; set; }
        public int WithLogo { get; set; }
        public int WithBanner { get; set; }
        public int WithDiscArt { get; set; }
        public int WithClearArt { get; set; }
        public int WithSeasonPoster { get; set; }
        public int WithSeasonThumb { get; set; }
        public int WithEpisodeStill { get; set; }
        public bool Pass =>
            EligibleCount == 0
            || (WithAnyFanart > 0
                && (!string.Equals(MediaType, "TV", StringComparison.OrdinalIgnoreCase)
                    || WithEpisodeStill > 0));
    }

    private sealed class DescriptionSourceCheckResult
    {
        public string Title { get; set; } = "";
        public string MediaType { get; set; } = "";
        public Guid EntityId { get; set; }
        public string DescriptionKey { get; set; } = "";
        public bool RequiresWikipediaDescription { get; set; }
        public bool RequiresRetailDescription { get; set; }
        public bool HasWikipediaDescription { get; set; }
        public bool HasRetailDescription { get; set; }
        public bool HasAnyDescription { get; set; }
        public bool CanonicalUsesWikipedia { get; set; }
        public string? CanonicalProvider { get; set; }
        public bool HasTagline { get; set; }
        public string? Detail { get; set; }

        public bool Pass =>
            HasAnyDescription
            && (!HasWikipediaDescription || CanonicalUsesWikipedia);
    }

    private sealed class OptionalArtworkState
    {
        public bool HasBackground { get; set; }
        public bool HasLogo { get; set; }
        public bool HasBanner { get; set; }
        public bool HasDiscArt { get; set; }
        public bool HasClearArt { get; set; }
        public bool HasSeasonPoster { get; set; }
        public bool HasSeasonThumb { get; set; }
        public bool HasEpisodeStill { get; set; }

        public void Merge(OptionalArtworkState other)
        {
            HasBackground |= other.HasBackground;
            HasLogo |= other.HasLogo;
            HasBanner |= other.HasBanner;
            HasDiscArt |= other.HasDiscArt;
            HasClearArt |= other.HasClearArt;
            HasSeasonPoster |= other.HasSeasonPoster;
            HasSeasonThumb |= other.HasSeasonThumb;
            HasEpisodeStill |= other.HasEpisodeStill;
        }

        public bool HasAny =>
            HasBackground
            || HasLogo
            || HasBanner
            || HasDiscArt
            || HasClearArt
            || HasSeasonPoster
            || HasSeasonThumb
            || HasEpisodeStill;
    }

    private sealed record PreferredArtworkRecord(
        string LocalImagePath,
        string? LocalImagePathSmall,
        string? LocalImagePathMedium,
        string? LocalImagePathLarge,
        string? PrimaryHex,
        string? SecondaryHex,
        string? AccentHex);

    private sealed record WorkHierarchyNode(Guid WorkId, Guid? ParentWorkId);

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

    // â”€â”€ Dynamic type selection + provider health â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

    // â”€â”€ Endpoint registration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public static void MapIntegrationTestEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/dev").WithTags("Development");

        group.MapPost("/integration-test", RunIntegrationTestAsync)
            .WithSummary("Full integration test: wipe â†’ seed â†’ ingest â†’ validate â†’ HTML report")
            .Produces(200, contentType: "text/html");
    }

    // â”€â”€ POST /dev/integration-test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task<IResult> RunIntegrationTestAsync(
        HttpContext context,
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        IIngestionEngine ingestionEngine,
        DevHarnessResetService resetService,
        IIdentityJobRepository identityJobRepo,
        ILibraryItemRepository libraryItemRepo,
        IReviewQueueRepository reviewRepo,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        IEnumerable<IExternalMetadataProvider> providers,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("IntegrationTest");
        var report = new TestReport();
        var sw = Stopwatch.StartNew();

        // â”€â”€ Parse optional stages parameter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // 1 = Stage 1 only (retail), 12 = Stage 1+2 (default), 123 = full pipeline
        int stages = 12;
        if (context.Request.Query.TryGetValue("stages", out var stagesParam) && int.TryParse(stagesParam, out var s))
            stages = s;
        report.StagesLevel = stages;

        DevHarnessWipeScope wipeScope;
        try
        {
            wipeScope = DevHarnessResetService.ParseScope(
                context.Request.Query.TryGetValue("wipeScope", out var scopeParam)
                    ? scopeParam.ToString()
                    : DevHarnessResetService.GeneratedStateScopeName);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        string stageLabel = stages switch
        {
            1 => "Stage 1 (Retail Identification only)",
            12 => "Stage 1+2 (Retail + Wikidata)",
            123 => "Stage 1+2+3 (Full pipeline including Universe Enrichment)",
            _ => $"Stage level {stages} (unknown â€” defaulting to 1+2)"
        };

        // â”€â”€ Parse optional types parameter â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var requestedTypes = ParseTypes(context);
        var health = await CheckProviderHealthAsync(logger);
        var (activeTypes, skipReasons) = ResolveActiveTypes(requestedTypes, health);
        report.ActiveTypes = activeTypes;
        report.SkippedTypes = skipReasons;
        report.ProviderHealth = health.ToDictionary(h => h.Key, h => h.Value.Healthy);

        string typesLabel = string.Join(", ", activeTypes.OrderBy(t => t));

        logger.LogInformation("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        logger.LogInformation("â•‘   INTEGRATION TEST â€” Starting            â•‘");
        logger.LogInformation("â•‘   {StageLabel}                           â•‘", stageLabel);
        logger.LogInformation("â•‘   Active types: {Types}                  â•‘", typesLabel);
        if (skipReasons.Count > 0)
            logger.LogInformation("â•‘   Skipped: {Skipped}                     â•‘",
                string.Join(", ", skipReasons.Select(s => $"{s.Key} ({s.Value})")));
        logger.LogInformation("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        try
        {
        // â”€â”€ Phase 1: Wipe â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 1] Wiping harness state ({Scope})...", wipeScope);
        try
        {
            DevHarnessResetResult resetResult = await resetService.WipeAsync(
                wipeScope,
                resumeWatcher: false,
                ct);
            report.WipeStatus = $"OK - {resetResult.Scope}";
        }
        catch (Exception ex)
        {
            report.WipeStatus = $"FAILED: {ex.Message}";
            report.IssuesFound.Add($"Wipe failed: {ex.Message}");
        }

        // â”€â”€ Phase 2: Seed â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 2] Seeding test files...");
        try
        {
            int seeded = await SeedInternalAsync(options, configLoader, activeTypes, logger);
            report.SeedStatus = $"OK â€” {seeded} files";
            report.TotalFilesSeeded = seeded;
        }
        catch (Exception ex)
        {
            report.SeedStatus = $"FAILED: {ex.Message}";
            report.IssuesFound.Add($"Seed failed: {ex.Message}");
        }

        // â”€â”€ Phase 3: Trigger scans and wait for ingestion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Brief settle to let file system flush.
        await Task.Delay(2000, ct);

        logger.LogInformation("[Phase 3] Triggering directory scans...");
        var scanConfig = configLoader.LoadLibraries();
        foreach (var lib in scanConfig.Libraries)
        {
            bool libraryIsActive =
                activeTypes.Contains(NormalizeHarnessMediaTypeKey(lib.Category))
                || lib.MediaTypes.Any(mt => activeTypes.Contains(NormalizeHarnessMediaTypeKey(mt)));

            if (!libraryIsActive)
                continue;

            var sourcePaths = lib.SourcePaths?.Where(path => !string.IsNullOrWhiteSpace(path)).ToList()
                              ?? [];
            if (sourcePaths.Count == 0 && !string.IsNullOrWhiteSpace(lib.SourcePath))
                sourcePaths.Add(lib.SourcePath);

            foreach (var sourcePath in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(sourcePath))
                    continue;

                int fileCount = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories).Length;
                ingestionEngine.ScanDirectory(sourcePath, lib.IncludeSubdirectories);
                logger.LogInformation("  Scan triggered: {Path} ({Category}, {Files} files)", sourcePath, lib.Category, fileCount);
            }
        }

        var ingestionTimeout = ResolveIngestionTimeout(configLoader, logger);
        logger.LogInformation("[Phase 3] Waiting for ingestion to complete (timeout: {Timeout})...", ingestionTimeout);
        var ingestionSw = Stopwatch.StartNew();
        bool ingestionComplete = await WaitForIngestionAsync(
            db,
            identityJobRepo,
            logger,
            ingestionTimeout,
            report.TotalFilesSeeded,
            stages,
            ct);
        ingestionSw.Stop();
        report.IngestionDuration = ingestionSw.Elapsed;

        if (!ingestionComplete)
        {
            report.IssuesFound.Add($"Ingestion did not complete within {ingestionTimeout}");
            logger.LogWarning("[Phase 3] Ingestion timeout â€” proceeding with partial results");
        }

        // â”€â”€ Phase 4: Validate results per media type â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 4] Validating ingestion results...");
        await ValidateResultsAsync(libraryItemRepo, report, logger, ct);

        // â”€â”€ Phase 4b: Vault Display Validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 4b] Vault display validation...");
            await ValidateVaultDisplayAsync(db, libraryItemRepo, report, stages, logger, ct);

        // â”€â”€ Phase 4c: File system and artwork validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (stages < 123)
        {
            logger.LogInformation("[Phase 4c] File system and artwork validation...");
            await ValidateFileSystemAsync(db, options, configLoader, libraryItemRepo, report, loggerFactory, logger, ct);
        }
        else
        {
            logger.LogInformation("[Phase 4c] Deferring file system validation until after Stage 3 artwork completes...");
        }

        // â”€â”€ Phase 4d: Stage Gating Validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 4d] Stage gating validation...");
        await ValidateStageGatingAsync(libraryItemRepo, report, stages, logger, ct);

        // â”€â”€ Phase 4e: Reconciliation â€” expected vs. actual outcomes â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 4e] Running reconciliation pass...");
        await RunReconciliationAsync(db, report, activeTypes, logger, ct);

        logger.LogInformation("[Phase 4f] Validating description source priority and fallback storage...");
        await ValidateDescriptionSourcesAsync(db, libraryItemRepo, report, logger, ct);

        // â”€â”€ Phase 5: Test manual search for review items â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 5] Testing manual search on review items...");
        await TestManualSearchAsync(libraryItemRepo, providers, report, logger, ct);

        // â”€â”€ Phase 6: Check universe enrichment â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        logger.LogInformation("[Phase 6] Checking universe enrichment...");
        await CheckUniversesAsync(libraryItemRepo, report, logger, ct);

        // â”€â”€ Phase 7: Stage 3 â€” Universe Enrichment (conditional) â”€â”€â”€â”€â”€â”€â”€â”€
        if (stages >= 123)
        {
            logger.LogInformation("[Phase 7] Triggering Stage 3 Universe Enrichment...");
            await RunStage3EnrichmentAsync(context, libraryItemRepo, db, report, logger, ct);
            await Task.Delay(TimeSpan.FromSeconds(5), ct);

            logger.LogInformation("[Phase 7b] File system and Stage 3 artwork validation...");
            report.FileSystemChecks.Clear();
            report.WatchFolderChecks.Clear();
            await ValidateFileSystemAsync(db, options, configLoader, libraryItemRepo, report, loggerFactory, logger, ct);
            ValidateStage3FanartAsync(report, logger);
            await ValidateCharacterArtworkAsync(db, canonicalArrayRepo, personRepo, report, logger, ct);
        }

        sw.Stop();
        report.TotalDuration = sw.Elapsed;

        // â”€â”€ Generate HTML report â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        string html = GenerateHtmlReport(report);

        // Save to disk â€” prefer repo root tools/reports/, fall back to CWD
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

        logger.LogInformation("â•”â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•—");
        logger.LogInformation("â•‘   INTEGRATION TEST â€” Complete            â•‘");
        logger.LogInformation("â•‘   Duration: {Duration}                   â•‘", report.TotalDuration);
        logger.LogInformation("â•‘   Result: {Result}                       â•‘", report.OverallPass ? "PASS" : "ISSUES FOUND");
        logger.LogInformation("â•šâ•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•");

        return Results.Content(html, "text/html");
        }
        finally
        {
            resetService.ResumeWatcher();
        }
    }

    // â”€â”€ Internal seed â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

    private static string NormalizeHarnessMediaTypeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().ToLowerInvariant() switch
        {
            "book" or "books" or "ebook" or "epub" => "books",
            "audiobook" or "audiobooks" => "audiobooks",
            "movie" or "movies" => "movies",
            "tv" or "television" => "tv",
            "music" => "music",
            "comic" or "comics" => "comics",
            var other => other,
        };
    }

    /// <summary>Known-fixture validation for movie-scoped actor-character display and verified portrait handling.</summary>
    private sealed class CharacterArtworkCheckResult
    {
        public string Title { get; set; } = "";
        public string WorkQid { get; set; } = "";
        public string ActorName { get; set; } = "";
        public string CharacterName { get; set; } = "";
        public bool WorkFound { get; set; }
        public bool HasMovieScopedActorCharacterLink { get; set; }
        public bool HasPortraitRow { get; set; }
        public string? PortraitSourceProvider { get; set; }
        public bool HasUnverifiedPortrait { get; set; }
        public bool HasDownloadedPortraitFile { get; set; }
        public bool HasDisplayCharacterName { get; set; }
        public bool HasDisplayCharacterImage { get; set; }
        public int? CastPosition { get; set; }
        public bool IsInPrimaryCastPreview { get; set; }
        public string? PortraitImageUrl { get; set; }
        public string? PortraitLocalImagePath { get; set; }
        public string? DisplayImageUrl { get; set; }
        public string? Detail { get; set; }
        public bool Pass =>
            WorkFound
            && HasMovieScopedActorCharacterLink
            && HasDisplayCharacterName
            && IsInPrimaryCastPreview
            && !HasUnverifiedPortrait;
    }

    private static TimeSpan ResolveIngestionTimeout(IConfigurationLoader configLoader, ILogger logger)
    {
        var fallback = TimeSpan.FromMinutes(20);

        try
        {
            var gate = configLoader.LoadCore().Pipeline.BatchGate;
            if (!gate.Enabled)
                return fallback;

            // Stage 2 may intentionally wait for the batch gate before making
            // Wikidata calls. A full all-types Stage 1+2+3 run also performs
            // quick hydration, write-back, person enrichment, and Stage 3 image
            // work, so the timeout has to cover more than the retail/bridge gate.
            var gatedTimeout = TimeSpan.FromSeconds(gate.TimeoutSeconds) + TimeSpan.FromMinutes(25);
            return gatedTimeout > fallback ? gatedTimeout : fallback;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Integration test: failed to read batch gate timeout; using {Fallback}", fallback);
            return fallback;
        }
    }

    // â”€â”€ Wait for ingestion â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task<bool> WaitForIngestionAsync(
        IDatabaseConnection db,
        IIdentityJobRepository identityJobRepo,
        ILogger logger,
        TimeSpan timeout,
        int expectedCount,
        int stages,
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
            string jobStateSummary;

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
                var jobStates = conn.Query<(string State, int Count)>("""
                    SELECT state AS State, COUNT(*) AS Count
                    FROM identity_jobs
                    GROUP BY state
                    ORDER BY state;
                    """);
                jobStateSummary = string.Join(", ", jobStates.Select(s => $"{s.State}:{s.Count}"));
            }

            int activeIdentityJobs = await CountActiveIdentityJobsAsync(db, identityJobRepo, stages, ct);
            sawExpectedAssetCount |= assetCount >= expectedCount;

            bool snapshotStable =
                assetCount == lastAssetCount
                && resolvedCount == lastResolvedCount
                && claimCount == lastClaimCount
                && pendingCount == lastPendingCount
                && activeIdentityJobs == lastActiveJobCount;

            stableSnapshots = snapshotStable ? stableSnapshots + 1 : 0;

            logger.LogInformation(
                "  Ingestion: assets={Assets}/{Expected}, resolved={Resolved}/{Works}, pending={Pending}, claims={Claims}, activeJobs={Jobs}, stable={Stable}/4, jobStates=[{JobStates}]",
                assetCount,
                expectedCount,
                resolvedCount,
                totalWorks,
                pendingCount,
                claimCount,
                activeIdentityJobs,
                stableSnapshots,
                jobStateSummary);

            if (assetCount >= expectedCount && totalWorks > 0 && pendingCount == 0 && activeIdentityJobs == 0 && stableSnapshots >= 2)
                return true;

            if (assetCount >= expectedCount && activeIdentityJobs == 0 && stableSnapshots >= 4)
                return true;

            if (sawExpectedAssetCount && activeIdentityJobs == 0 && stableSnapshots >= 6)
                return true;

            if (sawExpectedAssetCount && activeIdentityJobs > 0 && stableSnapshots >= 12)
            {
                logger.LogWarning(
                    "  Ingestion wait still waiting after {StableSnapshots} stable snapshots with {ActiveJobs} active identity job(s) and {Pending} pending work(s)",
                    stableSnapshots,
                    activeIdentityJobs,
                    pendingCount);
            }

            lastAssetCount = assetCount;
            lastResolvedCount = resolvedCount;
            lastClaimCount = claimCount;
            lastPendingCount = pendingCount;
            lastActiveJobCount = activeIdentityJobs;
        }

        if (lastActiveJobCount > 0)
        {
            logger.LogWarning(
                "  Ingestion wait timed out with {ActiveJobs} active identity job(s) remaining",
                lastActiveJobCount);
            return false;
        }

        return sawExpectedAssetCount || lastResolvedCount > 0;
    }

    // â”€â”€ Validate results â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task ValidateResultsAsync(
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Get all items
        var allItems = await libraryItemRepo.GetPageAsync(new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        report.TotalItems = allItems.TotalCount;

        logger.LogInformation("  Total items in libraryItem: {Count}", allItems.TotalCount);

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

                if (IsReviewStatus(item.Status))
                    mtResult.NeedsReview++;
                else if (IsFailureStatus(item.Status))
                    mtResult.Failed++;
                else if (IsIdentifiedStatus(item.Status))
                    mtResult.Identified++;
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

    // â”€â”€ Test manual search â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task TestManualSearchAsync(
        ILibraryItemRepository libraryItemRepo,
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

    // â”€â”€ Vault display validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task ValidateVaultDisplayAsync(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        int stages,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await libraryItemRepo.GetPageAsync(new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var validationItems = allItems.Items.Where(IsOwnedValidationItem).ToList();
        var expectations = DevSeedEndpoints.GetAllExpectations()
            .GroupBy(e => NormalizeExpectationKey(e.Title, e.MediaType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var providerNamesById = await LoadProviderNamesByIdAsync(db);

        foreach (var item in validationItems)
        {
            // Fetch detail to get creator fields (author/director/artist)
            var detail = await libraryItemRepo.GetDetailAsync(item.EntityId, ct);
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
                "Registered", "InReview", "Quarantined", "Rejected",
                "Queued", "RetailSearching", "RetailMatched", "RetailMatchedNeedsReview",
                "RetailNoMatch", "BridgeSearching", "QidResolved", "QidNeedsReview",
                "QidNoMatch", "Hydrating", "UniverseEnriching", "Ready",
                "ReadyWithoutUniverse", "Completed", "Failed",
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
                RequiresCreator = creatorRequired,
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
        if (passCount != report.LibraryChecks.Count)
        {
            report.IssuesFound.Add(
                $"Library display validation failed for {report.LibraryChecks.Count - passCount} item(s)");
        }

        // â”€â”€ Child entity validation (TV episodes, Music tracks) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        foreach (var item in validationItems)
        {
            if (string.IsNullOrWhiteSpace(item.WikidataQid)) continue;

            var detail = await libraryItemRepo.GetDetailAsync(item.EntityId, ct);
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
                    logger.LogInformation("  Child entities: TV '{Title}' â€” {Seasons} seasons, {Episodes} episodes",
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
                    logger.LogInformation("  Child entities: Music '{Title}' â€” {Tracks} tracks",
                        item.Title, Math.Max(trackCount, childCount));
            }
        }
    }

    // â”€â”€ Stage gating validation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task ValidateFileSystemAsync(
        IDatabaseConnection db,
        IOptions<IngestionOptions> options,
        IConfigurationLoader configLoader,
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        ILoggerFactory loggerFactory,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await libraryItemRepo.GetPageAsync(new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var validationItems = allItems.Items.Where(IsOwnedValidationItem).ToList();
        var organizer = new FileOrganizer(loggerFactory.CreateLogger<FileOrganizer>());
        var assetPathService = new AssetPathService(options.Value.LibraryRoot);
        var assetIds = await LoadWorkAssetIdsAsync(db);
        var assetCanonicals = await LoadCanonicalValueMapsAsync(db, assetIds.Values);
        var preferredCoverArtwork = await LoadPreferredArtworkRecordsAsync(db, "CoverArt");
        var assetIdsByPath = await LoadAssetIdsByPathAsync(db);
        var optionalArtworkStates = await LoadOptionalArtworkStatesAsync(db);
        var workHierarchy = await LoadWorkHierarchyAsync(db);
        var watchRoots = ResolveLeafSourcePaths(configLoader);
        var requireSidecarArtwork = ShouldRequireSidecarArtwork(configLoader.LoadCore().StoragePolicy);
        var expectations = DevSeedEndpoints.GetAllExpectations()
            .GroupBy(e => NormalizeExpectationKey(e.Title, e.MediaType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var item in validationItems)
        {
            var detail = await libraryItemRepo.GetDetailAsync(item.EntityId, ct);
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
                WikidataQid = item.WikidataQid,
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

                if (check.FileExists)
                    check.HasLegacyHeroSidecar = File.Exists(Path.Combine(Path.GetDirectoryName(filePath) ?? string.Empty, "hero.jpg"));

                if (check.FileExists && expectLibraryPlacement && expectedCoverArt)
                {
                    check.RequiresStoredArtwork = true;

                    if (requireSidecarArtwork)
                    {
                        check.RequiresSidecarArtwork = true;
                        check.HasPoster = HasAnySidecarPoster(filePath);
                        check.HasPosterThumb = HasAnySidecarPosterThumb(filePath);
                    }

                }
            }

            resolvedAssetId ??= assetIds.TryGetValue(item.EntityId, out var fallbackAssetId) ? fallbackAssetId : null;
            if (resolvedAssetId is Guid assetId && !string.IsNullOrWhiteSpace(options.Value.LibraryRoot))
            {
                var ownerEntityId = ResolveArtworkOwnerEntityId(item.EntityId, workHierarchy);
                check.HasStoredCover = HasCentralPreferredArtwork(
                    ownerEntityId,
                    preferredCoverArtwork,
                    assetPathService,
                    "Work",
                    "CoverArt");
                check.HasStoredCoverSmall = HasCentralArtworkRendition(ownerEntityId, preferredCoverArtwork, assetPathService, size: "s");
                check.HasStoredCoverMedium = HasCentralArtworkRendition(ownerEntityId, preferredCoverArtwork, assetPathService, size: "m");
                check.HasStoredCoverLarge = HasCentralArtworkRendition(ownerEntityId, preferredCoverArtwork, assetPathService, size: "l");
                check.HasStoredPalette = HasArtworkPalette(preferredCoverArtwork, ownerEntityId);
                check.HasStoredLegacyHero = File.Exists(assetPathService.GetCentralDerivedPath("Work", ownerEntityId, "hero", "hero.jpg"));

                if (assetCanonicals.TryGetValue(assetId, out var fanartMetadata))
                    check.HasFanartBridgeId = HasFanartBridgeId(fanartMetadata);
            }

            var optionalArtwork = ResolveOptionalArtworkState(item.EntityId, workHierarchy, optionalArtworkStates);
            check.HasStoredBackground = optionalArtwork.HasBackground;
            check.HasStoredLogo = optionalArtwork.HasLogo;
            check.HasStoredBanner = optionalArtwork.HasBanner;
            check.HasStoredDiscArt = optionalArtwork.HasDiscArt;
            check.HasStoredClearArt = optionalArtwork.HasClearArt;
            check.HasStoredSeasonPoster = optionalArtwork.HasSeasonPoster;
            check.HasStoredSeasonThumb = optionalArtwork.HasSeasonThumb;
            check.HasStoredEpisodeStill = optionalArtwork.HasEpisodeStill;

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
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        int stages,
        ILogger logger,
        CancellationToken ct)
    {
        var allItems = await libraryItemRepo.GetPageAsync(new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var validationItems = allItems.Items.Where(IsOwnedValidationItem).ToList();

        if (stages == 1)
        {
            // Stage 1 only: NO items should have a Wikidata QID
            foreach (var item in validationItems)
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
            foreach (var item in validationItems)
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
                        Check  = "Stage 2: retail match â†’ QID expected",
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
                        Check  = "Stage 2: no retail match â†’ QID not expected",
                        Pass   = true, // No retail match â€” QID absence is acceptable
                        Detail = hasQid ? $"Bonus QID: {item.WikidataQid}" : "No retail, no QID â€” expected",
                    };
                    report.StageGatingResults.Add(result);
                }
            }

            int pass = report.StageGatingResults.Count(r => r.Pass);
            logger.LogInformation("  Stage 1+2 gating: {Pass}/{Total} items pass gating checks",
                pass, report.StageGatingResults.Count);
        }
    }

    // â”€â”€ Stage 3: Universe Enrichment (conditional) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task RunStage3EnrichmentAsync(
        HttpContext context,
        ILibraryItemRepository libraryItemRepo,
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
                logger.LogWarning("[Phase 7] UniverseEnrichmentService not found in DI â€” skipping Stage 3");
                return;
            }

            universeService.TriggerManualSweep();
            logger.LogInformation("[Phase 7] Stage 3 manual sweep triggered â€” waiting for completion...");

            // Poll for universe/parent collection creation (timeout: 3 minutes)
            var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(3);
            int? lastCollectionCount = null;
            int stableCount = 0;
            while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                int collectionCount;
                using (var conn = db.CreateConnection())
                    collectionCount = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM collections WHERE parent_collection_id IS NOT NULL;");

                if (lastCollectionCount.HasValue && collectionCount == lastCollectionCount.Value) stableCount++;
                else stableCount = 0;
                lastCollectionCount = collectionCount;

                logger.LogInformation("  Stage 3: {Collections} collections with parent, stable={Stable}", collectionCount, stableCount);
                if (stableCount >= 3) break;
            }

            await WaitForArtworkActivityToSettleAsync(db, logger, TimeSpan.FromMinutes(2), ct);

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
                    logger.LogInformation("  Stage 3 Universe: '{Name}' â€” {Series} series, {Works} works, QID={Qid}",
                        ph.DisplayName, childCount, workCount, ph.WikidataQid ?? "none");
                }

                if (parentCollections.Count == 0)
                {
                    report.IssuesFound.Add("Stage 3: No parent collections (universes) created after enrichment");
                    logger.LogWarning("[Phase 7] No universes found after Stage 3 enrichment");
                }

                var fictionalEntityCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM fictional_entities;");
                var workLinkCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM fictional_entity_work_links;");
                var performerLinkCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM character_performer_links;");

                logger.LogInformation(
                    "  Stage 3 graph: {Entities} fictional entities, {WorkLinks} work links, {PerformerLinks} performer links",
                    fictionalEntityCount,
                    workLinkCount,
                    performerLinkCount);

                if (parentCollections.Count > 0 && fictionalEntityCount == 0)
                {
                    report.IssuesFound.Add("Stage 3: No fictional entities were stored after enrichment");
                    logger.LogWarning("[Phase 7] No fictional entities found after Stage 3 enrichment");
                }

                if (fictionalEntityCount > 0 && workLinkCount == 0)
                {
                    report.IssuesFound.Add("Stage 3: Fictional entities were stored but no work links were created");
                    logger.LogWarning("[Phase 7] No fictional entity work links found after Stage 3 enrichment");
                }
            }
        }
        catch (Exception ex)
        {
            report.IssuesFound.Add($"Stage 3 enrichment error: {ex.Message}");
            logger.LogError(ex, "[Phase 7] Stage 3 enrichment failed");
        }
    }

    // â”€â”€ Check universes â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task WaitForArtworkActivityToSettleAsync(
        IDatabaseConnection db,
        ILogger logger,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var lastEvidenceCount = -1;
        var stableCount = 0;

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);

            int evidenceCount;
            using (var conn = db.CreateConnection())
                evidenceCount = await conn.ExecuteScalarAsync<int>("""
                    SELECT
                        (SELECT COUNT(*) FROM entity_assets)
                        + (SELECT COUNT(*) FROM character_portraits
                           WHERE local_image_path IS NOT NULL
                             AND TRIM(local_image_path) <> '')
                    """);

            if (evidenceCount == lastEvidenceCount)
                stableCount++;
            else
                stableCount = 0;

            lastEvidenceCount = evidenceCount;
            if (stableCount >= 2)
                break;
        }

        logger.LogInformation("  Stage 3 artwork evidence settled at {Count}", Math.Max(lastEvidenceCount, 0));
    }

    private static async Task ValidateCharacterArtworkAsync(
        IDatabaseConnection db,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        report.CharacterArtworkChecks.Clear();

        if (!report.ActiveTypes.Contains("Movies"))
            return;

        var cases = new[]
        {
            new CharacterArtworkExpectation("The Shawshank Redemption", "Q172241", "Tim Robbins", "Andy Dufresne", "%Dufresne%"),
            new CharacterArtworkExpectation("The Matrix", "Q83495", "Keanu Reeves", "Neo", "%Neo%"),
        };

        foreach (var expected in cases)
            await ValidateCharacterArtworkCaseAsync(db, canonicalArrayRepo, personRepo, report, logger, expected, ct);
    }

    private static async Task ValidateCharacterArtworkCaseAsync(
        IDatabaseConnection db,
        ICanonicalValueArrayRepository canonicalArrayRepo,
        IPersonRepository personRepo,
        TestReport report,
        ILogger logger,
        CharacterArtworkExpectation expected,
        CancellationToken ct)
    {
        var check = new CharacterArtworkCheckResult
        {
            Title = expected.Title,
            WorkQid = expected.WorkQid,
            ActorName = expected.ActorName,
            CharacterName = expected.CharacterName,
        };

        using var conn = db.CreateConnection();
        var workIdText = await conn.ExecuteScalarAsync<string?>(
            """
            WITH work_assets AS (
                SELECT w.id AS work_id,
                       COALESCE(gp.id, p.id, w.id) AS root_work_id,
                       ma.id AS asset_id
                FROM works w
                LEFT JOIN works p ON p.id = w.parent_work_id
                LEFT JOIN works gp ON gp.id = p.parent_work_id
                INNER JOIN editions e ON e.work_id = w.id
                INNER JOIN media_assets ma ON ma.edition_id = e.id
            ),
            work_qids AS (
                SELECT wa.work_id,
                       COALESCE(
                           (SELECT w.wikidata_qid FROM works w
                            WHERE w.id = wa.work_id
                              AND w.wikidata_qid IS NOT NULL
                              AND TRIM(w.wikidata_qid) <> ''
                            LIMIT 1),
                           (SELECT cv.value FROM canonical_values cv
                            WHERE cv.entity_id = wa.asset_id AND cv.key = 'wikidata_qid' LIMIT 1),
                           (SELECT cv.value FROM canonical_values cv
                            WHERE cv.entity_id = wa.work_id AND cv.key = 'wikidata_qid' LIMIT 1),
                           (SELECT cv.value FROM canonical_values cv
                            WHERE cv.entity_id = wa.root_work_id AND cv.key = 'wikidata_qid' LIMIT 1),
                           (SELECT ij.resolved_qid FROM identity_jobs ij
                            WHERE ij.entity_id = wa.asset_id
                              AND ij.resolved_qid IS NOT NULL
                              AND TRIM(ij.resolved_qid) <> ''
                            ORDER BY ij.updated_at DESC, ij.created_at DESC
                            LIMIT 1)
                       ) AS qid
                FROM work_assets wa
            )
            SELECT work_id
            FROM work_qids
            WHERE qid = @workQid
            LIMIT 1;
            """,
            new { workQid = expected.WorkQid });

        check.WorkFound = Guid.TryParse(workIdText, out var workId);
        if (!check.WorkFound)
        {
            check.Detail = $"{expected.Title} work row was not found by Wikidata QID.";
            report.CharacterArtworkChecks.Add(check);
            report.IssuesFound.Add($"Character art: {expected.Title} was not found by QID {expected.WorkQid}");
            logger.LogWarning("[Phase 7c] Character art validation could not find {Title} ({Qid})", expected.Title, expected.WorkQid);
            return;
        }

        var row = await conn.QueryFirstOrDefaultAsync<CharacterArtworkRow>(
            """
            SELECT p.name                  AS ActorName,
                   fe.label                AS CharacterName,
                   fe.wikidata_qid         AS CharacterQid,
                   cp.id                   AS PortraitId,
                   cp.image_url            AS PortraitImageUrl,
                   cp.local_image_path     AS PortraitLocalImagePath,
                   cp.source_provider      AS PortraitSourceProvider,
                   cpl.work_qid            AS WorkQid
            FROM character_performer_links cpl
            INNER JOIN persons p
                ON p.id = cpl.person_id
            INNER JOIN fictional_entities fe
                ON fe.id = cpl.fictional_entity_id
            LEFT JOIN character_portraits cp
                ON cp.person_id = p.id
               AND cp.fictional_entity_id = fe.id
            WHERE cpl.work_qid = @workQid
              AND p.name = @actorName
              AND fe.label LIKE @characterPattern
            LIMIT 1;
            """,
            new
            {
                workQid = expected.WorkQid,
                actorName = expected.ActorName,
                characterPattern = expected.CharacterPattern,
            });

        check.HasMovieScopedActorCharacterLink = row is not null
            && string.Equals(row.WorkQid, expected.WorkQid, StringComparison.OrdinalIgnoreCase);
        check.HasPortraitRow = !string.IsNullOrWhiteSpace(row?.PortraitId);
        check.PortraitSourceProvider = row?.PortraitSourceProvider;
        check.HasUnverifiedPortrait = string.Equals(row?.PortraitSourceProvider, "tmdb_tagged_images", StringComparison.OrdinalIgnoreCase);
        check.PortraitImageUrl = row?.PortraitImageUrl;
        check.PortraitLocalImagePath = row?.PortraitLocalImagePath;
        check.HasDownloadedPortraitFile = !string.IsNullOrWhiteSpace(row?.PortraitLocalImagePath)
            && File.Exists(row.PortraitLocalImagePath);

        var cast = await CastCreditQueries.BuildForWorkAsync(workId, canonicalArrayRepo, personRepo, db, ct);
        var castEntry = cast
            .Select((credit, index) => new { Credit = credit, Position = index + 1 })
            .FirstOrDefault(entry =>
                string.Equals(entry.Credit.Name, expected.ActorName, StringComparison.OrdinalIgnoreCase));
        var timCredit = castEntry?.Credit;
        var portrayal = timCredit?.Characters.FirstOrDefault(character =>
            character.CharacterName?.Contains(expected.CharacterName, StringComparison.OrdinalIgnoreCase) == true);

        check.CastPosition = castEntry?.Position;
        check.IsInPrimaryCastPreview = check.CastPosition is > 0 and <= 5;
        check.HasDisplayCharacterName = portrayal is not null
            && string.Equals(timCredit?.Name, expected.ActorName, StringComparison.OrdinalIgnoreCase);
        check.DisplayImageUrl = portrayal?.PortraitUrl;
        check.HasDisplayCharacterImage = !string.IsNullOrWhiteSpace(portrayal?.PortraitUrl);

        check.Detail = check.Pass
            ? "Actor, character name, preview placement, and verified portrait handling are correct."
            : BuildCharacterArtworkFailureDetail(check);

        report.CharacterArtworkChecks.Add(check);

        logger.LogInformation(
            "  Character art: {Title} / {Actor} as {Character}: link={Link}, portrait={Portrait}, downloaded={Downloaded}, display={Display}",
            expected.Title,
            expected.ActorName,
            expected.CharacterName,
            check.HasMovieScopedActorCharacterLink,
            check.HasPortraitRow,
            check.HasDownloadedPortraitFile,
            check.HasDisplayCharacterImage);

        if (!check.Pass)
            report.IssuesFound.Add($"Character display: {expected.Title} does not safely display {expected.ActorName} as {expected.CharacterName} ({check.Detail})");
    }

    private sealed record CharacterArtworkExpectation(
        string Title,
        string WorkQid,
        string ActorName,
        string CharacterName,
        string CharacterPattern);

    private sealed class CharacterArtworkRow
    {
        public string? ActorName { get; init; }
        public string? CharacterName { get; init; }
        public string? CharacterQid { get; init; }
        public string? PortraitId { get; init; }
        public string? PortraitImageUrl { get; init; }
        public string? PortraitLocalImagePath { get; init; }
        public string? PortraitSourceProvider { get; init; }
        public string? WorkQid { get; init; }
    }

    private static string BuildCharacterArtworkFailureDetail(CharacterArtworkCheckResult check)
    {
        var missing = new List<string>();
        if (!check.WorkFound)
            missing.Add("work row");
        if (!check.HasMovieScopedActorCharacterLink)
            missing.Add("movie-scoped actor-character link");
        if (!check.HasDisplayCharacterName)
            missing.Add("detail cast character name");
        if (!check.IsInPrimaryCastPreview)
            missing.Add("top cast preview position");
        if (check.HasUnverifiedPortrait)
            missing.Add("unverified TMDB tagged character portrait");

        return missing.Count == 0
            ? "No missing checks were recorded."
            : "Missing: " + string.Join(", ", missing) + ".";
    }

    private static async Task CheckUniversesAsync(
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        // Check if items that should form universes got QIDs (prerequisite for universe formation)
        var allItems = await libraryItemRepo.GetPageAsync(new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);

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

    // â”€â”€ Reconciliation pass â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <summary>Dapper result row for the reconciliation SQL query.</summary>
    private sealed class WorkReconRow
    {
        public string TitleLower { get; set; } = "";
        public string MediaTypeLower { get; set; } = "";
        public string? WikidataQid { get; set; }
        public string? CuratorState { get; set; }
        public string? ReviewTrigger { get; set; }
        public bool HasStoredCoverArt { get; set; }
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

        // Build a lookup of (title_lower, media_type_lower) â†’ (wikidata_qid, curator_state, review_trigger)
        // from the live database. TV episodes share the same show title so we may have
        // duplicates â€” we treat any row for that (title, type) pair as "one Work" for
        // reconciliation purposes (first resolved row wins for identified check).

        // Dapper anonymous class for SQL projection
        List<WorkReconRow> dbRows;
        using (var conn = db.CreateConnection())
        {
            // For reconciliation, we need to match the seed-supplied title against
            // ANY title we can find for the work â€” the file processor's claim,
            // the canonical value (which may have been overridden by Wikidata),
            // alternate_title claims, original_title, etc. We emit one row per
            // (work, title-source) pair via UNION so the C# index can lookup
            // the work from any of its known titles.
            dbRows = (await conn.QueryAsync<WorkReconRow>(
                """
                WITH work_assets AS (
                    SELECT w.id AS work_id,
                           COALESCE(gp.id, p.id, w.id) AS root_work_id,
                           w.media_type,
                           w.curator_state,
                           ma.id AS asset_id
                    FROM works w
                    LEFT JOIN works p ON p.id = w.parent_work_id
                    LEFT JOIN works gp ON gp.id = p.parent_work_id
                    INNER JOIN editions e ON e.work_id = w.id
                    INNER JOIN media_assets ma ON ma.edition_id = e.id
                ),
                work_qids AS (
                    SELECT wa.work_id,
                           COALESCE(
                               (SELECT cv.value FROM canonical_values cv
                                WHERE cv.entity_id = wa.asset_id AND cv.key = 'wikidata_qid' LIMIT 1),
                               (SELECT cv.value FROM canonical_values cv
                                WHERE cv.entity_id = wa.work_id AND cv.key = 'wikidata_qid' LIMIT 1),
                               (SELECT cv.value FROM canonical_values cv
                                WHERE cv.entity_id = wa.root_work_id AND cv.key = 'wikidata_qid' LIMIT 1),
                               (SELECT ij.resolved_qid FROM identity_jobs ij
                                WHERE ij.entity_id = wa.asset_id
                                  AND ij.resolved_qid IS NOT NULL
                                  AND TRIM(ij.resolved_qid) <> ''
                                ORDER BY ij.updated_at DESC, ij.created_at DESC
                                LIMIT 1)
                           ) AS qid
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
                    SELECT wa.work_id,
                           wa.root_work_id,
                           wa.media_type,
                           wa.curator_state,
                           LOWER(cv.value) AS title_lower
                    FROM work_assets wa
                    INNER JOIN canonical_values cv ON cv.entity_id = wa.asset_id
                    WHERE cv.key IN ('title', 'original_title', 'show_name', 'series', 'episode_title', 'alternate_title', 'album')
                    UNION
                    -- File processor claim title (this is the seed-supplied title)
                    SELECT wa.work_id,
                           wa.root_work_id,
                           wa.media_type,
                           wa.curator_state,
                           LOWER(mc.claim_value) AS title_lower
                    FROM work_assets wa
                    INNER JOIN metadata_claims mc ON mc.entity_id = wa.asset_id
                    WHERE mc.claim_key IN ('title', 'original_title', 'show_name', 'series', 'episode_title', 'alternate_title', 'album')
                )
                SELECT DISTINCT
                    t.title_lower               AS TitleLower,
                    LOWER(t.media_type)         AS MediaTypeLower,
                    (SELECT qid FROM work_qids WHERE work_id = t.work_id) AS WikidataQid,
                    t.curator_state             AS CuratorState,
                    (SELECT trigger FROM work_reviews WHERE work_id = t.work_id) AS ReviewTrigger,
                    EXISTS(
                        SELECT 1
                        FROM entity_assets ea
                        WHERE ea.entity_id = t.root_work_id
                          AND ea.asset_type = 'CoverArt'
                          AND ea.is_preferred = 1
                          AND ea.local_image_path IS NOT NULL
                          AND TRIM(ea.local_image_path) <> ''
                    ) AS HasStoredCoverArt
                FROM titles t
                WHERE t.title_lower IS NOT NULL AND t.title_lower <> ''
                """)).AsList();
        }

        // Cover-art lookup built from the central managed asset store.
        // Keyed by "title_lower|media_type_lower" â†’ HasStoredCoverArt flag.
        var coverArtByKey = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in dbRows)
        {
            string key = $"{row.TitleLower}|{row.MediaTypeLower}";
            if (!coverArtByKey.TryGetValue(key, out var existing) || (row.HasStoredCoverArt && !existing))
                coverArtByKey[key] = row.HasStoredCoverArt;
        }

        var retailProviderByKey = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var check in report.LibraryChecks)
        {
            string k = $"{check.Title.ToLowerInvariant()}|{check.MediaType.ToLowerInvariant()}";
            if (!retailProviderByKey.ContainsKey(k) || !string.IsNullOrWhiteSpace(check.ActualRetailProvider))
                retailProviderByKey[k] = check.ActualRetailProvider;
        }

        // Index by "title_lower|media_type_lower" â†’ (wikidata_qid, curator_state, review_trigger)
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
            string titleLower = (exp.ReconciliationTitle ?? exp.Title).ToLowerInvariant();
            string mediaTypeLower = exp.MediaType.ToLowerInvariant();

            string expectedDesc = exp.ExpectIdentified
                ? (string.IsNullOrWhiteSpace(exp.ExpectedQid) ? "Identified" : $"Identified as {exp.ExpectedQid}")
                : $"InReview ({exp.ExpectedReviewTrigger ?? "any"})";

            // Not all active types were seeded â€” skip expectations for skipped types
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
                // Don't penalise for skipped types â€” exclude from reconciliation total
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
                    // Unresolved but expected InReview â€” treat as WrongTrigger
                    classification = "WrongTrigger";
                    actualDesc     = "Unresolved (no QID, no review entry)";
                }
            }

            // â”€â”€ Layered strictness checks â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            // Only run if the base classification was Match â€” a mismatched
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
                // MissingCoverArt: seed declares ExpectedCoverArt=true, but the
                // central managed asset store has no preferred cover for this title.
                // Only applies to identified items
                // (we never expect cover art on placeholder/review-queue entries).
                else if (exp.ExpectedCoverArt)
                {
                    string coverKey = $"{titleLower}|{mediaTypeLower}";
                    if (coverArtByKey.TryGetValue(coverKey, out var hasCover) && !hasCover)
                    {
                        classification = "MissingCoverArt";
                        actualDesc     = $"Identified{(actual.WikidataQid is { Length: > 0 } q ? $" as {q}" : "")} but no central stored artwork was persisted";
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
                logger.LogInformation("[Reconciliation] Match: '{Title}' ({Type}) â€” {Actual}",
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
                logger.LogWarning("[Reconciliation] {Class}: '{Title}' ({Type}) â€” expected={Expected}, actual={Actual}",
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

    // â”€â”€ HTML Report Generator â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task ValidateDescriptionSourcesAsync(
        IDatabaseConnection db,
        ILibraryItemRepository libraryItemRepo,
        TestReport report,
        ILogger logger,
        CancellationToken ct)
    {
        report.DescriptionSourceChecks.Clear();

        var allItems = await libraryItemRepo.GetPageAsync(
            new LibraryItemQuery(Offset: 0, Limit: 500, IncludeAll: true), ct);
        var validationItems = allItems.Items.Where(IsOwnedValidationItem).ToList();
        var providerNamesById = await LoadProviderNamesByIdAsync(db);
        var assetIdsByWork = await LoadWorkAssetIdsAsync(db);
        var hierarchy = await LoadWorkHierarchyAsync(db);

        using var conn = db.CreateConnection();
        foreach (var item in validationItems
            .Where(item => IsIdentifiedStatus(item.Status) && ShouldValidateDescriptionSources(item.MediaType))
            .OrderBy(item => item.MediaType)
            .ThenBy(item => item.Title))
        {
            ct.ThrowIfCancellationRequested();

            var descriptionKey = string.Equals(item.MediaType, "TV", StringComparison.OrdinalIgnoreCase)
                ? MetadataFieldConstants.EpisodeDescription
                : MetadataFieldConstants.Description;

            var entityIds = ResolveEntityScope(item.EntityId, assetIdsByWork, hierarchy);
            var keys = new[]
            {
                descriptionKey,
                MetadataFieldConstants.Description,
                MetadataFieldConstants.Tagline,
                MetadataFieldConstants.ShortDescription,
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var claims = (await conn.QueryAsync<DescriptionClaimRow>("""
                SELECT claim_key AS ClaimKey,
                       provider_id AS ProviderId
                FROM metadata_claims
                WHERE entity_id IN @entityIds
                  AND claim_key IN @keys
                  AND claim_value IS NOT NULL
                  AND TRIM(claim_value) <> '';
                """, new
            {
                entityIds = entityIds.Select(id => id.ToString()).ToArray(),
                keys,
            })).ToList();

            var canonicals = (await conn.QueryAsync<DescriptionCanonicalRow>("""
                SELECT key AS Key,
                       winning_provider_id AS WinningProviderId
                FROM canonical_values
                WHERE entity_id IN @entityIds
                  AND key IN @keys
                  AND value IS NOT NULL
                  AND TRIM(value) <> '';
                """, new
            {
                entityIds = entityIds.Select(id => id.ToString()).ToArray(),
                keys,
            })).ToList();

            var targetClaims = claims
                .Where(row => string.Equals(row.ClaimKey, descriptionKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var targetCanonicals = canonicals
                .Where(row => string.Equals(row.Key, descriptionKey, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var hasWikipediaDescription = targetClaims.Any(row => IsWikipediaProvider(row.ProviderId, providerNamesById));
            var hasRetailDescription = targetClaims.Any(row => IsRetailProvider(row.ProviderId, providerNamesById));
            var hasAnyDescription = targetClaims.Count > 0 || targetCanonicals.Count > 0;
            var canonicalWikipedia = targetCanonicals.Any(row => IsWikipediaProvider(row.WinningProviderId, providerNamesById));
            var canonicalProvider = targetCanonicals
                .Select(row => ProviderLabel(row.WinningProviderId, providerNamesById))
                .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label));

            var check = new DescriptionSourceCheckResult
            {
                Title = item.Title,
                MediaType = item.MediaType,
                EntityId = item.EntityId,
                DescriptionKey = descriptionKey,
                RequiresWikipediaDescription = !string.IsNullOrWhiteSpace(item.WikidataQid),
                RequiresRetailDescription = string.Equals(item.RetailMatch, "matched", StringComparison.OrdinalIgnoreCase),
                HasWikipediaDescription = hasWikipediaDescription,
                HasRetailDescription = hasRetailDescription,
                HasAnyDescription = hasAnyDescription,
                CanonicalUsesWikipedia = canonicalWikipedia,
                CanonicalProvider = canonicalProvider,
                HasTagline = claims.Any(row => string.Equals(row.ClaimKey, MetadataFieldConstants.Tagline, StringComparison.OrdinalIgnoreCase))
                    || canonicals.Any(row => string.Equals(row.Key, MetadataFieldConstants.Tagline, StringComparison.OrdinalIgnoreCase)),
            };

            if (!check.Pass)
            {
                check.Detail = BuildDescriptionCheckDetail(check);
                report.IssuesFound.Add(
                    $"Description source validation failed for {check.MediaType} '{check.Title}': {check.Detail}");
            }

            report.DescriptionSourceChecks.Add(check);
        }

        logger.LogInformation(
            "  Description source checks: {Pass}/{Total} passed",
            report.DescriptionSourceChecks.Count(check => check.Pass),
            report.DescriptionSourceChecks.Count);
    }

    private static string GenerateHtmlReport(TestReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>Tuvima Integration Test â€” {report.Timestamp:yyyy-MM-dd HH:mm}</title>");
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
        sb.AppendLine($"<h1>Tuvima Library â€” Integration Test Report {overallBadge}</h1>");
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
        sb.AppendLine($"<p class=\"subtitle\">{report.Timestamp:yyyy-MM-dd HH:mm:ss UTC} Â· Duration: {report.TotalDuration.TotalSeconds:F1}s Â· Ingestion: {report.IngestionDuration.TotalSeconds:F1}s Â· {stageBadge} Â· {typesBadge}</p>");

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
        if (report.DescriptionSourceChecks.Count > 0)
            SummaryCard(sb, report.DescriptionSourceChecks.Count(d => d.Pass) + "/" + report.DescriptionSourceChecks.Count, "Descriptions", "#34D399");
        if (report.Stage3FanartSummaries.Count > 0)
            SummaryCard(sb, report.Stage3FanartSummaries.Sum(s => s.WithAnyFanart) + "/" + report.Stage3FanartSummaries.Sum(s => s.EligibleCount), "Stage 3 Art", "#14B8A6");
        if (report.CharacterArtworkChecks.Count > 0)
            SummaryCard(sb, report.CharacterArtworkChecks.Count(c => c.Pass) + "/" + report.CharacterArtworkChecks.Count, "Character Art", "#F59E0B");
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
                string types = ProviderToTypes.TryGetValue(provider, out var ts) ? string.Join(", ", ts) : "â€”";
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
            sb.AppendLine($"<div class=\"media-type-header\"><span class=\"mt-dot\" style=\"background:{color}\"></span><strong>{Esc(mt.MediaType)}</strong> â€” {mt.Count} items ({mt.Identified} identified, {mt.NeedsReview} review, {mt.Failed} failed) {badge}</div>");

            if (mt.Items.Count > 0)
            {
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Title</th><th>Author</th><th>Year</th><th>Status</th><th>Confidence</th><th>Retail Match</th><th>QID</th><th>File</th></tr>");
                foreach (var item in mt.Items.OrderBy(i => i.Title))
                {
                    string statusClass = StatusClass(item.Status);
                    sb.AppendLine($"<tr><td>{Esc(item.Title)}</td><td>{Esc(item.Author ?? "â€”")}</td><td>{Esc(item.Year ?? "â€”")}</td>" +
                        $"<td class=\"{statusClass}\">{Esc(item.Status)}{(item.ReviewTrigger is not null ? $" <span class=\"mono\">({Esc(item.ReviewTrigger)})</span>" : "")}</td>" +
                        $"<td>{item.Confidence:P0}</td><td>{Esc(item.RetailMatch ?? "none")}</td>" +
                        $"<td class=\"mono\">{Esc(item.WikidataQid ?? "â€”")}</td><td class=\"mono\" style=\"max-width:200px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap\">{Esc(item.FileName)}</td></tr>");
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
            sb.AppendLine($"<div class=\"media-type-header\"><span class=\"mt-dot\" style=\"background:#94A3B8\"></span><strong>{Esc(type)}</strong> <span class=\"badge badge-skip\">SKIPPED â€” {Esc(reason)}</span></div>");
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
                $"<td>{s.ResultCount}</td><td>{Esc(s.TopResultTitle ?? "â€”")}</td><td>{s.TopResultConfidence:P0}</td><td>{badge}</td></tr>");
        }
        sb.AppendLine("</table>");

        // Universe enrichment results
        sb.AppendLine("<h2>Universe Enrichment</h2>");
        sb.AppendLine("<table>");
        sb.AppendLine("<tr><th>Universe</th><th>Works Found</th><th>QID Present</th><th>Sample QID</th><th>Status</th></tr>");
        foreach (var u in report.UniverseResults)
        {
            string badge = u.Found ? "<span class=\"badge badge-pass\">QID RESOLVED</span>" : "<span class=\"badge badge-warn\">NO QID YET</span>";
            sb.AppendLine($"<tr><td>{Esc(u.Name)}</td><td>{u.WorkCount}</td><td>{u.Found}</td><td class=\"mono\">{Esc(u.WikidataQid ?? "â€”")}</td><td>{badge}</td></tr>");
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
                    $"<td>{Check(v.HasExpectedRetailProvider)} <span class=\"mono\">{Esc(v.ActualRetailProvider ?? "Ã¢â‚¬â€")}</span></td>" +
                    $"<td>{Check(v.HasWikidataQid)} <span class=\"mono\">{Esc(v.WikidataQid ?? "")}</span></td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (report.DescriptionSourceChecks.Count > 0)
        {
            int descriptionPass = report.DescriptionSourceChecks.Count(d => d.Pass);
            string descriptionBadge = descriptionPass == report.DescriptionSourceChecks.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.DescriptionSourceChecks.Count - descriptionPass} ISSUES</span>";
            sb.AppendLine($"<h2>Description Source Validation {descriptionBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Key</th><th>Wikipedia</th><th>Retail</th><th>Canonical</th><th>Tagline</th><th>Status</th><th>Detail</th></tr>");
            foreach (var check in report.DescriptionSourceChecks.OrderBy(d => d.MediaType).ThenBy(d => d.Title))
            {
                string badge = check.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                sb.AppendLine($"<tr><td>{Esc(check.Title)}</td><td>{Esc(check.MediaType)}</td><td class=\"mono\">{Esc(check.DescriptionKey)}</td>" +
                    $"<td>{BoolMark(check.HasWikipediaDescription)}</td><td>{BoolMark(check.HasRetailDescription)}</td>" +
                    $"<td>{BoolMark(check.CanonicalUsesWikipedia)} <span class=\"mono\">{Esc(check.CanonicalProvider ?? "unknown")}</span></td>" +
                    $"<td>{BoolMark(check.HasTagline)}</td><td>{badge}</td><td>{Esc(check.Detail ?? "")}</td></tr>");
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
                        ? $"{BoolMark(check.HasPoster)}/{BoolMark(check.HasPosterThumb)}/{BoolMark(!check.HasLegacyHeroSidecar)}"
                        : $"n/a/{BoolMark(!check.HasLegacyHeroSidecar)}";
                    string stored = check.RequiresStoredArtwork
                        ? $"{BoolMark(check.HasStoredCover)}/{BoolMark(check.HasStoredCoverSmall)}/{BoolMark(check.HasStoredCoverMedium)}/{BoolMark(check.HasStoredCoverLarge)}/{BoolMark(check.HasStoredPalette)}/{BoolMark(!check.HasStoredLegacyHero)}"
                        : $"n/a/{BoolMark(!check.HasStoredLegacyHero)}";

                    sb.AppendLine($"<tr><td>{Esc(check.Title)}</td><td>{Esc(check.MediaType)}</td><td>{Esc(check.Status)}</td>" +
                        $"<td class=\"mono\">{Esc(check.ExpectedLocation)}{(!string.IsNullOrWhiteSpace(check.ExpectedRelativePath) ? "<br>" + Esc(check.ExpectedRelativePath) : "")}</td>" +
                        $"<td class=\"mono\">{Esc(check.ActualDisplayPath ?? check.ActualFilePath ?? "â€”")}</td>" +
                        $"<td class=\"mono\">{Esc(sidecars)}</td><td class=\"mono\">{Esc(stored)}</td><td>{Esc(check.Detail)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            sb.AppendLine($"<details{(failingChecks.Count == 0 ? " open" : "")}><summary style=\"cursor:pointer;color:#8B9DC3;font-weight:600\">All filesystem checks ({report.FileSystemChecks.Count})</summary>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Title</th><th>Media Type</th><th>Result</th><th>Expected</th><th>Actual</th><th>Template</th><th>Sidecars (P/T/NoHero)</th><th>Stored Core (O/S/M/L/Pal/NoHero)</th><th>Optional Stored Art (BG/LO/BA/DI/CL/SP/ST/EP)</th></tr>");
            foreach (var check in report.FileSystemChecks.OrderBy(f => f.MediaType).ThenBy(f => f.Title))
            {
                string result = check.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                string optionalArt =
                    $"{BoolMark(check.HasStoredBackground)}/{BoolMark(check.HasStoredLogo)}/{BoolMark(check.HasStoredBanner)}/" +
                    $"{BoolMark(check.HasStoredDiscArt)}/{BoolMark(check.HasStoredClearArt)}/" +
                    $"{BoolMark(check.HasStoredSeasonPoster)}/{BoolMark(check.HasStoredSeasonThumb)}/{BoolMark(check.HasStoredEpisodeStill)}";
                string sidecars = check.RequiresSidecarArtwork
                    ? $"{BoolMark(check.HasPoster)}/{BoolMark(check.HasPosterThumb)}/{BoolMark(!check.HasLegacyHeroSidecar)}"
                    : $"n/a/{BoolMark(!check.HasLegacyHeroSidecar)}";
                string storedCore = check.RequiresStoredArtwork
                    ? $"{BoolMark(check.HasStoredCover)}/{BoolMark(check.HasStoredCoverSmall)}/{BoolMark(check.HasStoredCoverMedium)}/{BoolMark(check.HasStoredCoverLarge)}/{BoolMark(check.HasStoredPalette)}/{BoolMark(!check.HasStoredLegacyHero)}"
                    : $"n/a/{BoolMark(!check.HasStoredLegacyHero)}";
                string templateState = check.RequiresTemplateMatch
                    ? BoolMark(check.PathMatchesTemplate)
                    : "n/a";

                sb.AppendLine($"<tr><td>{Esc(check.Title)}</td><td>{Esc(check.MediaType)}</td><td>{result}</td>" +
                    $"<td class=\"mono\">{Esc(check.ExpectedLocation)}{(!string.IsNullOrWhiteSpace(check.ExpectedRelativePath) ? "<br>" + Esc(check.ExpectedRelativePath) : "")}</td>" +
                    $"<td class=\"mono\">{Esc(check.ActualDisplayPath ?? check.ActualFilePath ?? "â€”")}</td>" +
                    $"<td class=\"mono\">{Esc(templateState)}</td><td class=\"mono\">{Esc(sidecars)}</td><td class=\"mono\">{Esc(storedCore)}</td><td class=\"mono\">{Esc(optionalArt)}</td></tr>");
            }
            sb.AppendLine("</table>");
            sb.AppendLine("</details>");
        }

        if (report.Stage3FanartSummaries.Count > 0)
        {
            int fanartPass = report.Stage3FanartSummaries.Count(s => s.Pass);
            string fanartBadge = fanartPass == report.Stage3FanartSummaries.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.Stage3FanartSummaries.Count - fanartPass} ISSUES</span>";
            sb.AppendLine($"<h2>Stage 3 Artwork Validation {fanartBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Media Type</th><th>Eligible Fanart Items</th><th>Any Fanart</th><th>Backgrounds</th><th>Logos</th><th>Banners</th><th>Disc Art</th><th>Clear Art</th><th>Season Posters</th><th>Season Thumbs</th><th>Episode Stills</th><th>Status</th></tr>");
            foreach (var summary in report.Stage3FanartSummaries.OrderBy(s => s.MediaType))
            {
                string badge = summary.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                sb.AppendLine($"<tr><td>{Esc(summary.MediaType)}</td><td>{summary.EligibleCount}</td><td>{summary.WithAnyFanart}</td><td>{summary.WithBackground}</td><td>{summary.WithLogo}</td><td>{summary.WithBanner}</td><td>{summary.WithDiscArt}</td><td>{summary.WithClearArt}</td><td>{summary.WithSeasonPoster}</td><td>{summary.WithSeasonThumb}</td><td>{summary.WithEpisodeStill}</td><td>{badge}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        if (report.CharacterArtworkChecks.Count > 0)
        {
            int characterArtPass = report.CharacterArtworkChecks.Count(c => c.Pass);
            string characterArtBadge = characterArtPass == report.CharacterArtworkChecks.Count
                ? "<span class=\"badge badge-pass\">ALL PASS</span>"
                : $"<span class=\"badge badge-warn\">{report.CharacterArtworkChecks.Count - characterArtPass} ISSUES</span>";
            sb.AppendLine($"<h2>Character Display Validation {characterArtBadge}</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Movie</th><th>Actor</th><th>Character</th><th>Movie Link</th><th>Portrait Row</th><th>Source</th><th>Unverified</th><th>Displayed Name</th><th>Displayed Image</th><th>Top Cast</th><th>Portrait Path</th><th>Status</th><th>Detail</th></tr>");
            foreach (var check in report.CharacterArtworkChecks.OrderBy(c => c.Title).ThenBy(c => c.ActorName))
            {
                string badge = check.Pass
                    ? "<span class=\"badge badge-pass\">PASS</span>"
                    : "<span class=\"badge badge-fail\">FAIL</span>";
                sb.AppendLine($"<tr><td>{Esc(check.Title)} <span class=\"mono\">{Esc(check.WorkQid)}</span></td>" +
                    $"<td>{Esc(check.ActorName)}</td><td>{Esc(check.CharacterName)}</td>" +
                    $"<td>{BoolMark(check.HasMovieScopedActorCharacterLink)}</td><td>{BoolMark(check.HasPortraitRow)}</td>" +
                    $"<td class=\"mono\">{Esc(check.PortraitSourceProvider ?? "none")}</td><td>{BoolMark(check.HasUnverifiedPortrait)}</td><td>{BoolMark(check.HasDisplayCharacterName)}</td>" +
                    $"<td>{BoolMark(check.HasDisplayCharacterImage)}</td><td>{BoolMark(check.IsInPrimaryCastPreview)} <span class=\"mono\">{Esc(check.CastPosition?.ToString() ?? "n/a")}</span></td><td class=\"mono\">{Esc(check.PortraitLocalImagePath ?? check.PortraitImageUrl ?? check.DisplayImageUrl ?? "none")}</td>" +
                    $"<td>{badge}</td><td>{Esc(check.Detail ?? "")}</td></tr>");
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
            sb.AppendLine($"<h2>Reconciliation â€” Seed Expectations vs. Pipeline Outcomes {reconBadge}</h2>");
            sb.AppendLine($"<p class=\"subtitle\">{recon.Matched}/{recon.ExpectedTotal} seed fixtures matched their expected outcome Â· ");
            sb.AppendLine(string.Join(" Â· ", recon.ByClassification
                .Where(kv => kv.Value > 0)
                .Select(kv => $"{Esc(kv.Key)}: {kv.Value}")));
            sb.AppendLine("</p>");

            // â”€â”€ Matched section (collapsed by default) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
            var matched = recon.ExpectedTotal - mismatches;
            sb.AppendLine($"<details><summary style=\"cursor:pointer;color:#5DCAA5;font-weight:600\">&#x2713; Matched Expected ({matched})</summary>");
            // Reconstruct matched items by re-running the DB data (we only stored mismatches)
            // Instead enumerate expectations again to build the full set, but since we only have
            // mismatches stored, derive matched count via total - mismatches. Show a simple
            // summary table for mismatches and a collapsed table for everything else.
            sb.AppendLine("<p style=\"color:rgba(255,255,255,0.4);font-size:12px;padding:8px 0\">" +
                $"{matched} item(s) produced the outcome declared in their seed fixture.</p>");
            sb.AppendLine("</details>");

            // â”€â”€ Mismatch sections by classification â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                        $"<td style=\"color:rgba(255,255,255,0.5);font-size:12px\">{Esc(item.Reason ?? "â€”")}</td>" +
                        $"</tr>");
                }
                sb.AppendLine("</table>");
                sb.AppendLine("</details>");
            }
        }

        // Footer
        sb.AppendLine($"<footer>Generated by Tuvima Library Integration Test Harness Â· {report.Timestamp:yyyy-MM-dd HH:mm:ss}</footer>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // â”€â”€ HTML helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void SummaryCard(StringBuilder sb, string num, string label, string color)
    {
        sb.AppendLine($"<div class=\"summary-card\"><div class=\"num\" style=\"color:{color}\">{num}</div><div class=\"label\">{label}</div></div>");
    }

    private static string Esc(string? s) =>
        System.Net.WebUtility.HtmlEncode(s ?? "");

    private static string BoolMark(bool value) => value ? "Y" : "N";

    private static string StatusClass(string? status)
    {
        if (IsReviewStatus(status)) return "status-review";
        if (IsFailureStatus(status)) return "status-failed";
        if (IsIdentifiedStatus(status)) return "status-identified";
        return "status-unknown";
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static async Task<int> CountActiveIdentityJobsAsync(
        IDatabaseConnection db,
        IIdentityJobRepository identityJobRepo,
        int stages,
        CancellationToken ct)
    {
        if (stages >= 123)
            return await identityJobRepo.CountActiveAsync(ct);

        ct.ThrowIfCancellationRequested();
        using var conn = db.CreateConnection();
        return await conn.ExecuteScalarAsync<int>("""
            SELECT COUNT(*)
            FROM identity_jobs
            WHERE state NOT IN (
                'Ready',
                'ReadyWithoutUniverse',
                'Completed',
                'Failed',
                'RetailNoMatch',
                'QidNoMatch',
                'QidNeedsReview',
                'UniverseEnriching'
            );
            """);
    }

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

    private static async Task<Dictionary<Guid, OptionalArtworkState>> LoadOptionalArtworkStatesAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string EntityId, string AssetType, string LocalImagePath)>("""
            SELECT entity_id        AS EntityId,
                   asset_type       AS AssetType,
                   local_image_path AS LocalImagePath
            FROM entity_assets
            WHERE entity_type = 'Work'
              AND asset_type IN ('Background', 'Logo', 'Banner', 'DiscArt', 'ClearArt', 'SeasonPoster', 'SeasonThumb', 'EpisodeStill')
              AND local_image_path IS NOT NULL
              AND TRIM(local_image_path) <> '';
            """);

        var result = new Dictionary<Guid, OptionalArtworkState>();
        foreach (var row in rows)
        {
            if (!Guid.TryParse(row.EntityId, out var workId))
                continue;
            if (string.IsNullOrWhiteSpace(row.LocalImagePath) || !File.Exists(row.LocalImagePath))
                continue;

            if (!result.TryGetValue(workId, out var state))
            {
                state = new OptionalArtworkState();
                result[workId] = state;
            }

            switch (row.AssetType)
            {
                case "Background":
                    state.HasBackground = true;
                    break;
                case "Logo":
                    state.HasLogo = true;
                    break;
                case "Banner":
                    state.HasBanner = true;
                    break;
                case "DiscArt":
                    state.HasDiscArt = true;
                    break;
                case "ClearArt":
                    state.HasClearArt = true;
                    break;
                case "SeasonPoster":
                    state.HasSeasonPoster = true;
                    break;
                case "SeasonThumb":
                    state.HasSeasonThumb = true;
                    break;
                case "EpisodeStill":
                    state.HasEpisodeStill = true;
                    break;
            }
        }

        return result;
    }

    private static async Task<Dictionary<Guid, WorkHierarchyNode>> LoadWorkHierarchyAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<(string Id, string? ParentWorkId)>("""
            SELECT id             AS Id,
                   parent_work_id AS ParentWorkId
            FROM works;
            """);

        return rows
            .Where(row => Guid.TryParse(row.Id, out _))
            .ToDictionary(
                row => Guid.Parse(row.Id),
                row => new WorkHierarchyNode(
                    Guid.Parse(row.Id),
                    Guid.TryParse(row.ParentWorkId, out var parentWorkId) ? parentWorkId : null));
    }

    private static OptionalArtworkState ResolveOptionalArtworkState(
        Guid workId,
        IReadOnlyDictionary<Guid, WorkHierarchyNode> hierarchy,
        IReadOnlyDictionary<Guid, OptionalArtworkState> states)
    {
        var merged = new OptionalArtworkState();
        var visited = new HashSet<Guid>();
        Guid? current = workId;

        while (current.HasValue && visited.Add(current.Value))
        {
            if (states.TryGetValue(current.Value, out var state))
                merged.Merge(state);

            current = hierarchy.TryGetValue(current.Value, out var node)
                ? node.ParentWorkId
                : null;
        }

        return merged;
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

    private static async Task<Dictionary<Guid, PreferredArtworkRecord>> LoadPreferredArtworkRecordsAsync(
        IDatabaseConnection db,
        string assetType)
    {
        using var conn = db.CreateConnection();
        var columns = (await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('entity_assets');"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (columns.Count == 0)
        {
            return [];
        }

        var rows = await conn.QueryAsync<(string EntityId, string LocalImagePath, string? LocalImagePathSmall, string? LocalImagePathMedium, string? LocalImagePathLarge, string? PrimaryHex, string? SecondaryHex, string? AccentHex)>($"""
            SELECT entity_id        AS EntityId,
                   local_image_path AS LocalImagePath,
                   {ResolveEntityAssetColumnSql(columns, "LocalImagePathSmall", "local_image_path_s", "local_image_path_small")},
                   {ResolveEntityAssetColumnSql(columns, "LocalImagePathMedium", "local_image_path_m", "local_image_path_medium")},
                   {ResolveEntityAssetColumnSql(columns, "LocalImagePathLarge", "local_image_path_l", "local_image_path_large")},
                   {ResolveEntityAssetColumnSql(columns, "PrimaryHex", "primary_hex")},
                   {ResolveEntityAssetColumnSql(columns, "SecondaryHex", "secondary_hex")},
                   {ResolveEntityAssetColumnSql(columns, "AccentHex", "accent_hex")}
            FROM entity_assets
            WHERE asset_type = @assetType
              AND is_preferred = 1
              AND local_image_path IS NOT NULL
              AND TRIM(local_image_path) <> '';
            """, new { assetType });

        return rows
            .Where(row => Guid.TryParse(row.EntityId, out _))
            .ToDictionary(
                row => Guid.Parse(row.EntityId),
                row => new PreferredArtworkRecord(
                    row.LocalImagePath,
                    row.LocalImagePathSmall,
                    row.LocalImagePathMedium,
                    row.LocalImagePathLarge,
                    row.PrimaryHex,
                    row.SecondaryHex,
                    row.AccentHex),
                EqualityComparer<Guid>.Default);
    }

    private static string ResolveEntityAssetColumnSql(
        IReadOnlySet<string> columns,
        string alias,
        params string[] candidates)
    {
        var match = candidates.FirstOrDefault(columns.Contains);
        return match is null
            ? $"NULL AS {alias}"
            : $"{match} AS {alias}";
    }

    private static Guid ResolveArtworkOwnerEntityId(
        Guid workId,
        IReadOnlyDictionary<Guid, WorkHierarchyNode> hierarchy)
    {
        var current = workId;
        var visited = new HashSet<Guid>();

        while (visited.Add(current)
               && hierarchy.TryGetValue(current, out var node)
               && node.ParentWorkId.HasValue)
        {
            current = node.ParentWorkId.Value;
        }

        return current;
    }

    private static bool HasCentralPreferredArtwork(
        Guid ownerEntityId,
        IReadOnlyDictionary<Guid, PreferredArtworkRecord> preferredArtworkRecords,
        AssetPathService assetPathService,
        string ownerKind,
        string assetType)
    {
        if (!preferredArtworkRecords.TryGetValue(ownerEntityId, out var artwork)
            || string.IsNullOrWhiteSpace(artwork.LocalImagePath)
            || !File.Exists(artwork.LocalImagePath))
        {
            return false;
        }

        var artworkDirectory = GetCentralArtworkDirectory(assetPathService, ownerKind, ownerEntityId, assetType);
        return artwork.LocalImagePath.StartsWith(artworkDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCentralArtworkRendition(
        Guid ownerEntityId,
        IReadOnlyDictionary<Guid, PreferredArtworkRecord> preferredArtworkRecords,
        AssetPathService assetPathService,
        string size)
    {
        if (!preferredArtworkRecords.TryGetValue(ownerEntityId, out var artwork))
            return false;

        var renditionPath = size switch
        {
            "s" => artwork.LocalImagePathSmall,
            "m" => artwork.LocalImagePathMedium,
            "l" => artwork.LocalImagePathLarge,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(renditionPath) || !File.Exists(renditionPath))
        {
            return false;
        }

        return renditionPath.StartsWith(assetPathService.DerivedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasArtworkPalette(
        IReadOnlyDictionary<Guid, PreferredArtworkRecord> preferredArtworkRecords,
        Guid ownerEntityId)
    {
        if (!preferredArtworkRecords.TryGetValue(ownerEntityId, out var artwork))
            return false;

        return !string.IsNullOrWhiteSpace(artwork.PrimaryHex)
               && !string.IsNullOrWhiteSpace(artwork.SecondaryHex)
               && !string.IsNullOrWhiteSpace(artwork.AccentHex);
    }

    private static bool ShouldRequireSidecarArtwork(LibraryStoragePolicy storagePolicy) =>
        storagePolicy.ArtworkExport || storagePolicy.ExportProfile.Artwork;

    private static string GetCentralArtworkDirectory(
        AssetPathService assetPathService,
        string ownerKind,
        Guid ownerEntityId,
        string assetType)
    {
        var samplePath = assetPathService.GetCentralAssetPath(ownerKind, ownerEntityId, assetType, Guid.NewGuid(), ".jpg");
        return Path.GetDirectoryName(samplePath) ?? string.Empty;
    }

    private static string NormalizeExpectationKey(string title, string mediaType) =>
        $"{title.Trim().ToLowerInvariant()}|{mediaType.Trim().ToLowerInvariant()}";

    private sealed class DescriptionClaimRow
    {
        public string ClaimKey { get; set; } = "";
        public string ProviderId { get; set; } = "";
    }

    private sealed class DescriptionCanonicalRow
    {
        public string Key { get; set; } = "";
        public string? WinningProviderId { get; set; }
    }

    private static IReadOnlyList<Guid> ResolveEntityScope(
        Guid workId,
        IReadOnlyDictionary<Guid, Guid> assetIdsByWork,
        IReadOnlyDictionary<Guid, WorkHierarchyNode> hierarchy)
    {
        var ids = new List<Guid> { workId };
        if (assetIdsByWork.TryGetValue(workId, out var assetId))
            ids.Add(assetId);

        var visited = new HashSet<Guid> { workId };
        Guid? current = workId;
        while (current.HasValue
            && hierarchy.TryGetValue(current.Value, out var node)
            && node.ParentWorkId is { } parentId
            && visited.Add(parentId))
        {
            ids.Add(parentId);
            current = parentId;
        }

        return ids.Distinct().ToList();
    }

    private static bool IsIdentifiedStatus(string? status)
    {
        var value = status ?? "";
        return value.Contains("Identified", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Confirmed", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Registered", StringComparison.OrdinalIgnoreCase)
            || value.Equals("RetailMatched", StringComparison.OrdinalIgnoreCase)
            || value.Equals("BridgeSearching", StringComparison.OrdinalIgnoreCase)
            || value.Equals("QidResolved", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Hydrating", StringComparison.OrdinalIgnoreCase)
            || value.Equals("UniverseEnriching", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Ready", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ReadyWithoutUniverse", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnedValidationItem(LibraryCatalogItem item) =>
        !string.IsNullOrWhiteSpace(item.FilePath)
        || item.IsReadyForLibrary
        || string.Equals(item.LibraryVisibility, "review_only", StringComparison.OrdinalIgnoreCase);

    private static bool IsWikipediaProvider(
        string? providerId,
        IReadOnlyDictionary<Guid, string> providerNamesById)
    {
        if (!Guid.TryParse(providerId, out var id))
            return false;

        if (id == WellKnownProviders.Wikidata || id == WellKnownProviders.Wikipedia)
            return true;

        return providerNamesById.TryGetValue(id, out var name)
            && (name.Contains("wikidata", StringComparison.OrdinalIgnoreCase)
                || name.Contains("wikipedia", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRetailProvider(
        string? providerId,
        IReadOnlyDictionary<Guid, string> providerNamesById)
    {
        if (!Guid.TryParse(providerId, out var id))
            return false;

        if (id == WellKnownProviders.AppleApi
            || id == WellKnownProviders.OpenLibrary
            || id == WellKnownProviders.GoogleBooks
            || id == WellKnownProviders.Tmdb
            || id == WellKnownProviders.ComicVine
            || id == WellKnownProviders.MusicBrainz)
        {
            return true;
        }

        return providerNamesById.TryGetValue(id, out var name)
            && !IsNonRetailProvider(name)
            && (name.Contains("apple", StringComparison.OrdinalIgnoreCase)
                || name.Contains("open_library", StringComparison.OrdinalIgnoreCase)
                || name.Contains("google_books", StringComparison.OrdinalIgnoreCase)
                || name.Contains("tmdb", StringComparison.OrdinalIgnoreCase)
                || name.Contains("comic", StringComparison.OrdinalIgnoreCase)
                || name.Contains("musicbrainz", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ProviderLabel(
        string? providerId,
        IReadOnlyDictionary<Guid, string> providerNamesById)
    {
        if (!Guid.TryParse(providerId, out var id))
            return null;

        return providerNamesById.TryGetValue(id, out var name)
            ? name
            : id.ToString();
    }

    private static string BuildDescriptionCheckDetail(DescriptionSourceCheckResult check)
    {
        var missing = new List<string>();
        if (!check.HasAnyDescription)
            missing.Add($"{check.DescriptionKey} missing");
        if (check.HasWikipediaDescription && !check.CanonicalUsesWikipedia)
            missing.Add($"canonical winner is {check.CanonicalProvider ?? "unknown"}, expected Wikipedia/Wikidata");

        return missing.Count == 0 ? "unknown description source issue" : string.Join("; ", missing);
    }

    private static bool ShouldValidateDescriptionSources(string? mediaType)
    {
        // Music tracks do not need per-song prose. If we add music description
        // validation later, it should target album-level metadata only.
        return !string.Equals(mediaType, "Music", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ProviderNameRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> LoadProviderNamesByIdAsync(IDatabaseConnection db)
    {
        using var conn = db.CreateConnection();
        var rows = await conn.QueryAsync<ProviderNameRow>(
            "SELECT id AS Id, name AS Name FROM metadata_providers");

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
        LibraryItemDetail detail,
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
            || value.Equals("RetailNoMatch", StringComparison.OrdinalIgnoreCase)
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
        LibraryCatalogItem item,
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

    private static void ValidateStage3FanartAsync(TestReport report, ILogger logger)
    {
        report.Stage3FanartSummaries.Clear();

        foreach (var mediaType in new[] { "Movies", "TV", "Music" })
        {
            var eligible = report.FileSystemChecks
                .Where(check =>
                    string.Equals(check.MediaType, mediaType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(check.ExpectedLocation, "Library", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(check.WikidataQid)
                    && check.HasFanartBridgeId
                    && check.FileExists
                    && check.LocationMatchesExpectation)
                .ToList();

            if (eligible.Count == 0)
                continue;

            var summary = new Stage3FanartSummary
            {
                MediaType = mediaType,
                EligibleCount = eligible.Count,
                WithAnyFanart = eligible.Count(check =>
                    check.HasStoredBackground
                    || check.HasStoredLogo
                    || check.HasStoredBanner
                    || check.HasStoredDiscArt
                    || check.HasStoredClearArt
                    || check.HasStoredSeasonPoster
                    || check.HasStoredSeasonThumb
                    || check.HasStoredEpisodeStill),
                WithBackground = eligible.Count(check => check.HasStoredBackground),
                WithLogo = eligible.Count(check => check.HasStoredLogo),
                WithBanner = eligible.Count(check => check.HasStoredBanner),
                WithDiscArt = eligible.Count(check => check.HasStoredDiscArt),
                WithClearArt = eligible.Count(check => check.HasStoredClearArt),
                WithSeasonPoster = eligible.Count(check => check.HasStoredSeasonPoster),
                WithSeasonThumb = eligible.Count(check => check.HasStoredSeasonThumb),
                WithEpisodeStill = eligible.Count(check => check.HasStoredEpisodeStill),
            };

            report.Stage3FanartSummaries.Add(summary);
            logger.LogInformation(
                "  Stage 3 fanart: {MediaType} {WithAny}/{Eligible} items have stored fanart evidence",
                summary.MediaType,
                summary.WithAnyFanart,
                summary.EligibleCount);

            if (!summary.Pass)
            {
                var reason = string.Equals(summary.MediaType, "TV", StringComparison.OrdinalIgnoreCase)
                    && summary.WithEpisodeStill == 0
                    ? "no stored episode stills were created"
                    : "no stored optional artwork assets were created";
                report.IssuesFound.Add(
                    $"Stage 3 fanart: {reason} for eligible {summary.MediaType} items");
            }
        }
    }

    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Equals(
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

    private static bool HasFanartBridgeId(IReadOnlyDictionary<string, string> metadata)
    {
        static bool HasValue(IReadOnlyDictionary<string, string> map, params string[] keys) =>
            keys.Any(key => map.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value));

        return HasValue(
            metadata,
            BridgeIdKeys.TmdbId,
            "tmdb_movie_id",
            "tmdb_tv_id",
            BridgeIdKeys.TvdbId,
            BridgeIdKeys.MusicBrainzId,
            "musicbrainz_artist_id",
            BridgeIdKeys.MusicBrainzReleaseGroupId);
    }

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
        if (check.HasLegacyHeroSidecar)
            return "Legacy hero.jpg sidecar still exists next to the media file";
        if (check.HasStoredLegacyHero)
            return "Legacy hero.jpg still exists in the central asset store";
        if (check.RequiresSidecarArtwork && (!check.HasPoster || !check.HasPosterThumb))
            return "Sidecar artwork is incomplete next to the media file";
        if (check.RequiresStoredArtwork && (!check.HasStoredCover || !check.HasStoredCoverSmall || !check.HasStoredCoverMedium || !check.HasStoredCoverLarge || !check.HasStoredPalette))
            return "Stored artwork renditions or palette metadata are incomplete in the central asset store";

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




    private static string SanitizeFileName(string title)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (char c in title) sb.Append(invalid.Contains(c) ? '_' : c);
        return sb.ToString();
    }
}
