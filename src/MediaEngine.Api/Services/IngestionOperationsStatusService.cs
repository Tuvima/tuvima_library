using Dapper;
using MediaEngine.Api.Models;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Models;
using Microsoft.Extensions.Options;
using ProviderConfig = MediaEngine.Storage.Models.ProviderConfiguration;

namespace MediaEngine.Api.Services;

public sealed class IngestionOperationsStatusService : IIngestionOperationsStatusService
{
    private static readonly string[] ActiveBatchStatuses = ["running", "queued", "processing", "active"];
    private static readonly string[] FailedBatchStatuses = ["failed", "abandoned"];

    private readonly IDatabaseConnection _db;
    private readonly IConfigurationLoader _configLoader;
    private readonly IProviderHealthRepository _providerHealth;
    private readonly IIngestionBatchRepository _batchRepository;
    private readonly ILibraryItemRepository _libraryItems;
    private readonly IOptions<IngestionOptions> _ingestionOptions;

    public IngestionOperationsStatusService(
        IDatabaseConnection db,
        IConfigurationLoader configLoader,
        IProviderHealthRepository providerHealth,
        IIngestionBatchRepository batchRepository,
        ILibraryItemRepository libraryItems,
        IOptions<IngestionOptions> ingestionOptions)
    {
        _db = db;
        _configLoader = configLoader;
        _providerHealth = providerHealth;
        _batchRepository = batchRepository;
        _libraryItems = libraryItems;
        _ingestionOptions = ingestionOptions;
    }

    public async Task<IngestionOperationsSnapshotDto> GetSnapshotAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var recentBatches = await _batchRepository.GetRecentAsync(12, ct);
        var lifecycle = await _libraryItems.GetFourStateCountsAsync(ct: ct);
        var projection = await _libraryItems.GetProjectionSummaryAsync(ct);
        var providers = _configLoader.LoadAllProviders();
        var healthRecords = await _providerHealth.GetAllAsync(ct);
        var healthById = healthRecords.ToDictionary(h => h.ProviderId, StringComparer.OrdinalIgnoreCase);

        using var conn = _db.CreateConnection();
        var pipelineRows = (await conn.QueryAsync<StageCountRow>("""
            SELECT state AS Key, COUNT(*) AS Count
            FROM identity_jobs
            GROUP BY state
            """)).ToDictionary(r => r.Key, r => ToInt(r.Count), StringComparer.OrdinalIgnoreCase);

        var ingestionRows = (await conn.QueryAsync<StageCountRow>("""
            SELECT status AS Key, COUNT(*) AS Count
            FROM ingestion_log
            GROUP BY status
            """)).ToDictionary(r => r.Key, r => ToInt(r.Count), StringComparer.OrdinalIgnoreCase);

        var reviewRows = (await conn.QueryAsync<ReviewCountRow>("""
            SELECT trigger AS Trigger, detail AS Detail, COUNT(*) AS Count
            FROM review_queue
            WHERE status = 'Pending'
            GROUP BY trigger, detail
            """)).AsList();

        var folderStats = (await conn.QueryAsync<FolderStatsRow>("""
            SELECT
                COALESCE(source_path, '') AS SourcePath,
                MAX(started_at) AS LastScan,
                COALESCE(SUM(files_registered), 0) AS ItemCount,
                COALESCE(SUM(files_review + files_no_match + files_failed), 0) AS UnresolvedCount
            FROM ingestion_batches
            WHERE source_path IS NOT NULL AND source_path <> ''
            GROUP BY source_path
            """)).ToDictionary(r => r.SourcePath, StringComparer.OrdinalIgnoreCase);

        var activeJobs = recentBatches
            .Where(batch => ActiveBatchStatuses.Contains(batch.Status, StringComparer.OrdinalIgnoreCase))
            .Select(ToActiveJob)
            .ToList();

        var providerDtos = providers
            .Where(IsIngestionProvider)
            .OrderBy(p => ProviderSortKey(p.Name))
            .ThenBy(p => p.Name)
            .Select(p => ToProviderDto(p, healthById.GetValueOrDefault(p.Name)))
            .ToList();

        var providerWarnings = providerDtos.Count(p =>
            p.Status is "Degraded" or "Offline" or "Missing Configuration");

        var failedJobs = recentBatches.Count(batch =>
            FailedBatchStatuses.Contains(batch.Status, StringComparer.OrdinalIgnoreCase))
            + Count(pipelineRows, nameof(IdentityJobState.Failed));

        var summary = new IngestionOperationsSummaryDto
        {
            TotalItems = projection.TotalItems,
            RegisteredItems = lifecycle.Identified,
            ProvisionalItems = lifecycle.Provisional,
            ItemsNeedingReview = lifecycle.InReview,
            ActiveJobs = activeJobs.Count,
            FailedJobs = failedJobs,
            ProviderWarnings = providerWarnings,
            LastSuccessfulScanTime = recentBatches
                .Where(batch => batch.CompletedAt.HasValue && !batch.Status.Contains("fail", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(batch => batch.CompletedAt)
                .Select(batch => batch.CompletedAt)
                .FirstOrDefault(),
            EngineStatus = providerWarnings > 0 || failedJobs > 0 ? "Degraded" : "Online",
            HealthLabel = ResolveHealthLabel(lifecycle.InReview, providerWarnings, failedJobs, activeJobs.Count),
        };

        return new IngestionOperationsSnapshotDto
        {
            Summary = summary,
            ActiveJobs = activeJobs,
            PipelineStages = BuildPipelineStages(pipelineRows, ingestionRows, lifecycle, projection),
            ReviewReasons = BuildReviewReasons(reviewRows, lifecycle.TriggerCounts),
            SourceGroups = BuildSourceGroups(folderStats),
            ProviderHealth = providerDtos,
            RecentBatches = recentBatches.Select(ToRecentBatch).ToList(),
            Organization = BuildOrganizationRules(),
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    private List<IngestionSourceGroupDto> BuildSourceGroups(IReadOnlyDictionary<string, FolderStatsRow> folderStats)
    {
        var groups = new Dictionary<string, Dictionary<string, List<IngestionSourceFolderDto>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Watch"] = new(StringComparer.OrdinalIgnoreCase),
            ["Listen"] = new(StringComparer.OrdinalIgnoreCase),
            ["Read"] = new(StringComparer.OrdinalIgnoreCase),
        };

        foreach (var library in _configLoader.LoadLibraries().Libraries)
        {
            var category = NormalizeCategory(library.Category);
            var intent = ResolveIntent(category);
            if (!groups.TryGetValue(intent, out var libraries))
            {
                libraries = new(StringComparer.OrdinalIgnoreCase);
                groups[intent] = libraries;
            }

            if (!libraries.TryGetValue(category, out var folders))
            {
                folders = [];
                libraries[category] = folders;
            }

            foreach (var path in EffectiveSourcePaths(library))
            {
                var status = ProbeFolder(path);
                var stats = folderStats.GetValueOrDefault(path);
                folders.Add(new IngestionSourceFolderDto
                {
                    Path = path,
                    MediaType = string.Join(", ", library.MediaTypes.DefaultIfEmpty(category)),
                    Purpose = ResolvePurpose(library),
                    ScanMode = library.IntakeMode.Equals("watch", StringComparison.OrdinalIgnoreCase)
                        ? "automatic"
                        : "manual",
                    LastScan = ParseDate(stats?.LastScan),
                    ItemCount = stats is null ? 0 : ToInt(stats.ItemCount),
                    UnresolvedCount = stats is null ? 0 : ToInt(stats.UnresolvedCount),
                    Status = status.Label,
                    IsReachable = status.IsReachable,
                    PermissionsValid = status.PermissionsValid,
                    MusicNote = category.Equals("Music", StringComparison.OrdinalIgnoreCase)
                        ? "Preserve album folders; prefer tags and fingerprints before organization."
                        : null,
                });
            }
        }

        return groups
            .Select(group => new IngestionSourceGroupDto
            {
                Intent = group.Key,
                Libraries = group.Value
                    .Select(library => new IngestionSourceLibraryDto
                    {
                        Label = library.Key,
                        Folders = library.Value,
                    })
                    .Where(library => library.Folders.Count > 0)
                    .ToList(),
            })
            .ToList();
    }

    private IngestionOrganizationRulesDto BuildOrganizationRules()
    {
        var options = _ingestionOptions.Value;
        var folderTemplate = FirstNonBlank(options.OrganizationTemplate, _configLoader.LoadCore().OrganizationTemplate, "Not configured");
        var filenameTemplate = Path.GetFileName(folderTemplate.Replace('/', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(filenameTemplate))
        {
            filenameTemplate = folderTemplate;
        }

        return new IngestionOrganizationRulesDto
        {
            RenameEnabled = options.AutoOrganize,
            MoveEnabled = options.AutoOrganize,
            PreviewRequired = true,
            FolderTemplateSummary = folderTemplate,
            FilenameTemplateSummary = filenameTemplate,
            LastOrganizationRun = null,
            MusicBehavior = "Music uses conservative organization and should preserve album folders unless a library rule says otherwise.",
        };
    }

    private static List<IngestionPipelineStageDto> BuildPipelineStages(
        IReadOnlyDictionary<string, int> pipelineRows,
        IReadOnlyDictionary<string, int> ingestionRows,
        LibraryItemLifecycleCounts lifecycle,
        LibraryItemProjectionSummary projection) =>
    [
        Stage("detected", "Detected", Count(ingestionRows, "detected"), "Files found in watched folders"),
        Stage("parsed", "Parsed", Count(ingestionRows, "processed") + Count(ingestionRows, "scored"), "Names and embedded metadata interpreted"),
        Stage("identified", "Identified", Count(pipelineRows, nameof(IdentityJobState.RetailMatched)) + Count(pipelineRows, nameof(IdentityJobState.RetailMatchedNeedsReview)) + lifecycle.Identified, "Recognized as a media item"),
        Stage("matched", "Matched", Count(pipelineRows, nameof(IdentityJobState.RetailMatched)) + Count(pipelineRows, nameof(IdentityJobState.QidResolved)), "Matched with metadata providers"),
        Stage("canonicalized", "Canonicalized", projection.WithQid, "Linked to canonical identity when available"),
        Stage("enriched", "Enriched", projection.EnrichedStage3, "Artwork, people, series, genres, universes added"),
        Stage("organized", "Organized", Count(ingestionRows, "registered"), "Rename and folder operations completed"),
        Stage("registered", "Registered", lifecycle.Identified, "Ready in the library"),
        Stage("needs_review", "Needs Review", lifecycle.InReview, "Waiting for a curator decision"),
        Stage("failed", "Failed", Count(pipelineRows, nameof(IdentityJobState.Failed)) + Count(ingestionRows, "failed"), "Could not finish automatically"),
    ];

    private static List<IngestionReviewReasonDto> BuildReviewReasons(
        IReadOnlyList<ReviewCountRow> reviewRows,
        IReadOnlyDictionary<string, int> triggerCounts)
    {
        var allCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in reviewRows)
        {
            allCounts[row.Trigger] = allCounts.GetValueOrDefault(row.Trigger) + ToInt(row.Count);
            if (!string.IsNullOrWhiteSpace(row.Detail))
            {
                allCounts[row.Detail] = allCounts.GetValueOrDefault(row.Detail) + ToInt(row.Count);
            }
        }

        foreach (var kv in triggerCounts)
        {
            allCounts[kv.Key] = Math.Max(allCounts.GetValueOrDefault(kv.Key), kv.Value);
        }

        return
        [
            Review("unmatched", "Unmatched", SumMatching(allCounts, "RetailMatchFailed", "AuthorityMatchFailed", "ContentMatchFailed", "StagedUnidentifiable"), "Items could not be matched to a known catalogue record."),
            Review("low_confidence", "Low Confidence", SumMatching(allCounts, "LowConfidence", "RetailMatchAmbiguous", "ArbiterNeedsReview"), "Items matched below the confidence threshold."),
            Review("duplicates", "Duplicates", SumContains(allCounts, "duplicate"), "Possible duplicate files or editions need confirmation."),
            Review("missing_artwork", "Missing Artwork", SumMatching(allCounts, "ArtworkUnconfirmed") + SumContains(allCounts, "artwork", "cover"), "Artwork is missing or needs confirmation."),
            Review("missing_wikidata", "Missing Wikidata Identity", SumMatching(allCounts, "MissingQid", "WikidataBridgeFailed", "MultipleQidMatches"), "Items need canonical identity or Wikidata confirmation."),
            Review("naming", "Naming Issues", SumMatching(allCounts, "RootWatchFolder", "PlaceholderTitle", "AmbiguousMediaType") + SumContains(allCounts, "naming", "folder"), "File or folder names do not provide enough context."),
            Review("provider_failures", "Provider Failures", SumMatching(allCounts, "WritebackFailed") + SumContains(allCounts, "provider", "timeout", "failure"), "A provider or writeback step could not complete."),
            Review("metadata_conflicts", "Metadata Conflicts", SumMatching(allCounts, "MetadataConflict", "UserFixMatch", "LanguageMismatch", "UserReport"), "Conflicting metadata requires a human decision."),
        ];
    }

    private static IngestionOperationsJobDto ToActiveJob(IngestionBatch batch)
    {
        var percent = batch.FilesTotal > 0
            ? Math.Clamp(batch.FilesProcessed * 100d / batch.FilesTotal, 0, 100)
            : 0;

        return new IngestionOperationsJobDto
        {
            JobId = batch.Id,
            JobType = "Ingestion batch",
            MediaType = batch.Category,
            SourceFolder = batch.SourcePath,
            CurrentStage = ResolveBatchStage(batch),
            CurrentItem = null,
            ProcessedCount = batch.FilesProcessed,
            TotalCount = batch.FilesTotal,
            PercentComplete = percent,
            Status = batch.Status,
            Elapsed = DateTimeOffset.UtcNow - batch.StartedAt.ToUniversalTime(),
            LastUpdatedTime = batch.UpdatedAt,
            WarningSummary = batch.FilesFailed > 0 ? $"{batch.FilesFailed:N0} failed" : null,
        };
    }

    private static IngestionOperationsBatchDto ToRecentBatch(IngestionBatch batch)
    {
        var source = FirstNonBlank(batch.Category, ShortPath(batch.SourcePath), "Library scan");
        return new IngestionOperationsBatchDto
        {
            BatchId = batch.Id,
            StartedAt = batch.StartedAt,
            CompletedAt = batch.CompletedAt,
            Source = source,
            MediaType = batch.Category,
            TotalFiles = batch.FilesTotal,
            RegisteredCount = batch.FilesIdentified,
            ReviewCount = batch.FilesReview + batch.FilesNoMatch,
            FailedCount = batch.FilesFailed,
            Status = batch.Status,
            Summary = $"{batch.FilesIdentified:N0} registered, {batch.FilesReview + batch.FilesNoMatch:N0} review, {batch.FilesFailed:N0} failed",
        };
    }

    private static IngestionProviderHealthDto ToProviderDto(ProviderConfig provider, ProviderHealthRecord? health)
    {
        var status = !provider.Enabled
            ? "Disabled"
            : health?.Status switch
            {
                ProviderHealthStatus.Healthy => "Healthy",
                ProviderHealthStatus.Degraded => "Degraded",
                ProviderHealthStatus.Down => "Offline",
                _ => ProviderLooksConfigured(provider) ? "Unknown" : "Missing Configuration",
            };

        return new IngestionProviderHealthDto
        {
            ProviderId = provider.Name,
            DisplayName = DisplayProviderName(provider.Name),
            Status = status,
            LastSuccessfulCall = health?.LastSuccessAt,
            LastError = health?.LastFailureReason,
            Warning = health is { ConsecutiveFailures: > 0 }
                ? $"{health.ConsecutiveFailures} consecutive failure(s)"
                : provider.ThrottleMs > 0 ? $"Throttled at {provider.ThrottleMs}ms" : null,
        };
    }

    private static bool IsIngestionProvider(ProviderConfig provider)
    {
        var id = provider.Name.ToLowerInvariant();
        if (id.Contains("apple") || id.Contains("tmdb") || id.Contains("musicbrainz")
            || id.Contains("wikidata") || id.Contains("wikipedia") || id.Contains("comic")
            || id.Contains("google") || id.Contains("open_library") || id.Contains("audible"))
        {
            return true;
        }

        return provider.CapabilityTags.Any(tag =>
            tag.Contains("cover", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("identity", StringComparison.OrdinalIgnoreCase)
            || tag.Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ProviderLooksConfigured(ProviderConfig provider) =>
        provider.Endpoints.Count > 0 || provider.AvailableFields.Count > 0 || provider.CapabilityTags.Count > 0;

    private static FolderProbe ProbeFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new("Unavailable", null, null);
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return new("Unreachable", false, false);
            }

            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
            return new("Reachable", true, true);
        }
        catch (UnauthorizedAccessException)
        {
            return new("Permission issue", true, false);
        }
        catch (IOException)
        {
            return new("Unavailable", false, false);
        }
    }

    private static IReadOnlyList<string> EffectiveSourcePaths(LibraryFolderConfig library)
    {
        if (library.SourcePaths is { Count: > 0 })
        {
            return library.SourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
        }

        return string.IsNullOrWhiteSpace(library.SourcePath) ? [] : [library.SourcePath];
    }

    private static string ResolveIntent(string category) => category.ToLowerInvariant() switch
    {
        "movies" or "movie" or "tv" or "tv shows" or "shows" => "Watch",
        "music" or "audiobooks" or "audiobook" or "podcasts" => "Listen",
        "books" or "book" or "comics" or "comic" => "Read",
        _ => "Read",
    };

    private static string NormalizeCategory(string category) => category.Trim() switch
    {
        "" => "Library",
        "Movie" => "Movies",
        "TV" => "TV Shows",
        "Audiobook" => "Audiobooks",
        "Book" => "Books",
        "Comic" => "Comics",
        var value => value,
    };

    private static string ResolvePurpose(LibraryFolderConfig library)
    {
        if (library.ReadOnly)
        {
            return "archive";
        }

        return library.IntakeMode.Equals("import", StringComparison.OrdinalIgnoreCase)
            ? "incoming"
            : "primary";
    }

    private static string ResolveBatchStage(IngestionBatch batch)
    {
        if (batch.FilesFailed > 0)
        {
            return "Attention needed";
        }

        if (batch.FilesReview + batch.FilesNoMatch > 0)
        {
            return "Review decisions";
        }

        if (batch.FilesIdentified > 0)
        {
            return "Registering library items";
        }

        return "Scanning source folder";
    }

    private static string ResolveHealthLabel(int review, int providerWarnings, int failedJobs, int activeJobs)
    {
        if (failedJobs > 0 || providerWarnings > 0)
        {
            return "Needs attention";
        }

        if (review > 0)
        {
            return "Review needed";
        }

        return activeJobs > 0 ? "Working" : "Healthy";
    }

    private static int ProviderSortKey(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("apple")) return 0;
        if (lower.Contains("tmdb")) return 1;
        if (lower.Contains("musicbrainz")) return 2;
        if (lower.Contains("wikidata")) return 3;
        if (lower.Contains("wikipedia")) return 4;
        if (lower.Contains("comic")) return 5;
        return 20;
    }

    private static string DisplayProviderName(string name) => name.Replace("_", " ") switch
    {
        "apple api" => "Apple Books",
        "tmdb" => "TMDB",
        "musicbrainz" => "MusicBrainz",
        "wikidata reconciliation" => "Wikidata",
        "google books" => "Google Books",
        "open library" => "Open Library",
        var value => System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(value),
    };

    private static IngestionPipelineStageDto Stage(string key, string label, int count, string helper) =>
        new() { Key = key, Label = label, Count = Math.Max(0, count), Helper = helper };

    private static IngestionReviewReasonDto Review(string key, string label, int count, string explanation) =>
        new() { Key = key, Label = label, Count = Math.Max(0, count), Explanation = explanation };

    private static int Count(IReadOnlyDictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var count) ? count : 0;

    private static int ToInt(long value) =>
        value > int.MaxValue ? int.MaxValue : (int)Math.Max(0, value);

    private static int SumMatching(IReadOnlyDictionary<string, int> counts, params string[] keys) =>
        keys.Sum(key => Count(counts, key));

    private static int SumContains(IReadOnlyDictionary<string, int> counts, params string[] needles) =>
        counts
            .Where(kv => needles.Any(needle => kv.Key.Contains(needle, StringComparison.OrdinalIgnoreCase)))
            .Sum(kv => kv.Value);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string ShortPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('\\', '/');
        var index = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private sealed class StageCountRow
    {
        public string Key { get; set; } = "";
        public long Count { get; set; }
    }

    private sealed class ReviewCountRow
    {
        public string Trigger { get; set; } = "";
        public string? Detail { get; set; }
        public long Count { get; set; }
    }

    private sealed class FolderStatsRow
    {
        public string SourcePath { get; set; } = "";
        public string? LastScan { get; set; }
        public long ItemCount { get; set; }
        public long UnresolvedCount { get; set; }
    }
    private sealed record FolderProbe(string Label, bool? IsReachable, bool? PermissionsValid);
}
