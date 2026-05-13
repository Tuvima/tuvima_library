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
    private static readonly TimeSpan NoWorkBatchGrace = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan NoWorkBatchMaximumAge = TimeSpan.FromMinutes(2);
    private static readonly string[] RetailMatchedStates =
    [
        nameof(IdentityJobState.RetailMatched),
        nameof(IdentityJobState.BridgeSearching),
        nameof(IdentityJobState.QidResolved),
        nameof(IdentityJobState.QidNeedsReview),
        nameof(IdentityJobState.QidNoMatch),
        nameof(IdentityJobState.Hydrating),
        nameof(IdentityJobState.UniverseEnriching),
        nameof(IdentityJobState.Ready),
        nameof(IdentityJobState.ReadyWithoutUniverse),
        nameof(IdentityJobState.Completed),
    ];
    private static readonly string[] RetailReviewStates = [nameof(IdentityJobState.RetailMatchedNeedsReview)];
    private static readonly string[] RetailNoMatchStates = [nameof(IdentityJobState.RetailNoMatch)];
    private static readonly string[] WikidataResolvedStates =
    [
        nameof(IdentityJobState.QidResolved),
        nameof(IdentityJobState.Hydrating),
        nameof(IdentityJobState.UniverseEnriching),
        nameof(IdentityJobState.Ready),
        nameof(IdentityJobState.ReadyWithoutUniverse),
        nameof(IdentityJobState.Completed),
    ];
    private static readonly string[] WikidataReviewStates =
    [
        nameof(IdentityJobState.QidNeedsReview),
        nameof(IdentityJobState.QidNoMatch),
    ];
    private static readonly string[] EnrichmentCompleteStates =
    [
        nameof(IdentityJobState.Ready),
        nameof(IdentityJobState.ReadyWithoutUniverse),
        nameof(IdentityJobState.Completed),
    ];
    private static readonly TimeSpan ActiveActivityFreshness = TimeSpan.FromMinutes(6);
    private static readonly string[] CurrentActivityStates =
    [
        nameof(IdentityJobState.RetailSearching),
        nameof(IdentityJobState.BridgeSearching),
        nameof(IdentityJobState.Hydrating),
        nameof(IdentityJobState.UniverseEnriching),
    ];
    private const int ActivityBatchSize = 50;

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
        recentBatches = await ReconcileCompletedBatchesAsync(recentBatches, ct);
        var displayBatch = SelectDisplayBatch(recentBatches);
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
        var scopedPipelineRows = displayBatch is null
            ? pipelineRows
            : await ReadIdentityStateCountsAsync(displayBatch.Id, ct);
        var scopedIngestionRows = displayBatch is null
            ? ingestionRows
            : await ReadIngestionStatusCountsAsync(displayBatch.Id, ct);

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

        var activeJobs = new List<IngestionOperationsJobDto>();
        foreach (var batch in recentBatches
            .Where(batch => ActiveBatchStatuses.Contains(batch.Status, StringComparer.OrdinalIgnoreCase))
            .Where(ShouldShowActiveBatch))
        {
            var batchPipelineRows = displayBatch?.Id == batch.Id
                ? scopedPipelineRows
                : await ReadIdentityStateCountsAsync(batch.Id, ct);
            var batchIngestionRows = displayBatch?.Id == batch.Id
                ? scopedIngestionRows
                : await ReadIngestionStatusCountsAsync(batch.Id, ct);
            var batchStages = BuildPipelineStages(batchPipelineRows, batchIngestionRows, lifecycle, projection, batch);
            activeJobs.Add(ToActiveJob(batch, batchPipelineRows, batchStages));
        }

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
        var summaryTotals = BuildSummaryTotals(displayBatch, lifecycle, projection);
        var pipelineStages = BuildPipelineStages(scopedPipelineRows, scopedIngestionRows, lifecycle, projection, displayBatch);
        var currentActivities = displayBatch is null
            ? []
            : await ReadTaskActivitiesAsync(displayBatch.Id, pipelineStages, ct);
        if (currentActivities.Count == 0)
        {
            currentActivities = activeJobs
                .Take(3)
                .Select(job => ToCurrentActivity(job, pipelineStages))
                .Where(activity => !string.IsNullOrWhiteSpace(activity.Message))
                .ToList();
        }

        var activeWorkCount = activeJobs.Count > 0
            ? activeJobs.Count
            : currentActivities.Count > 0 ? 1 : 0;

        var summary = new IngestionOperationsSummaryDto
        {
            TotalItems = summaryTotals.Total,
            RegisteredItems = summaryTotals.Registered,
            ProvisionalItems = lifecycle.Provisional,
            ItemsNeedingReview = summaryTotals.Review,
            ActiveJobs = activeWorkCount,
            FailedJobs = failedJobs,
            ProviderWarnings = providerWarnings,
            LastSuccessfulScanTime = recentBatches
                .Where(batch => batch.CompletedAt.HasValue && !batch.Status.Contains("fail", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(batch => batch.CompletedAt)
                .Select(batch => batch.CompletedAt)
                .FirstOrDefault(),
            EngineStatus = providerWarnings > 0 || failedJobs > 0 ? "Degraded" : "Online",
            HealthLabel = ResolveHealthLabel(summaryTotals.Review, providerWarnings, failedJobs, activeWorkCount),
        };

        return new IngestionOperationsSnapshotDto
        {
            Summary = summary,
            ActiveJobs = activeJobs,
            CurrentActivities = currentActivities,
            PipelineStages = pipelineStages,
            ReviewReasons = BuildReviewReasons(reviewRows, lifecycle.TriggerCounts),
            SourceGroups = BuildSourceGroups(folderStats),
            ProviderHealth = providerDtos,
            RecentBatches = recentBatches.Select(ToRecentBatch).ToList(),
            Organization = BuildOrganizationRules(),
            GeneratedAt = DateTimeOffset.UtcNow,
        };
    }

    private async Task<IReadOnlyDictionary<string, int>> ReadIdentityStateCountsAsync(Guid batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<StageCountRow>("""
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            )
            SELECT state AS Key, COUNT(*) AS Count
            FROM latest_jobs
            WHERE rn = 1
            GROUP BY state
            """, new { batchId = batchId.ToString() });

        return rows.ToDictionary(r => r.Key, r => ToInt(r.Count), StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyDictionary<string, int>> ReadIngestionStatusCountsAsync(Guid batchId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<StageCountRow>("""
            SELECT status AS Key, COUNT(*) AS Count
            FROM ingestion_log
            WHERE ingestion_run_id = @batchId
            GROUP BY status
            """, new { batchId = batchId.ToString() });

        return rows.ToDictionary(r => r.Key, r => ToInt(r.Count), StringComparer.OrdinalIgnoreCase);
    }

    private static IngestionBatch? SelectDisplayBatch(IReadOnlyList<IngestionBatch> recentBatches)
    {
        return recentBatches.FirstOrDefault(HasOutcomeCounters)
            ?? recentBatches.FirstOrDefault(batch => batch.FilesProcessed > 0)
            ?? recentBatches.FirstOrDefault();
    }

    private static BatchSummaryTotals BuildSummaryTotals(
        IngestionBatch? displayBatch,
        LibraryItemLifecycleCounts lifecycle,
        LibraryItemProjectionSummary projection)
    {
        if (displayBatch is null)
        {
            return new(projection.TotalItems, lifecycle.Identified, lifecycle.InReview);
        }

        return new(
            Math.Max(0, displayBatch.FilesTotal),
            Math.Max(0, displayBatch.FilesIdentified),
            Math.Max(0, displayBatch.FilesReview + displayBatch.FilesNoMatch + displayBatch.FilesFailed));
    }

    private static bool ShouldShowActiveBatch(IngestionBatch batch)
    {
        if (HasOutcomeCounters(batch) || batch.FilesProcessed > 0)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - batch.StartedAt.ToUniversalTime() <= NoWorkBatchGrace;
    }

    private static bool HasOutcomeCounters(IngestionBatch batch) =>
        batch.FilesIdentified + batch.FilesReview + batch.FilesNoMatch + batch.FilesFailed > 0;

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
        LibraryItemProjectionSummary projection,
        IngestionBatch? displayBatch)
    {
        var batchTotal = Math.Max(0, displayBatch?.FilesTotal ?? projection.TotalItems);
        var batchProcessed = Math.Max(0, displayBatch?.FilesProcessed ?? lifecycle.Identified + lifecycle.InReview + lifecycle.Provisional + lifecycle.Rejected);
        var duplicates = Count(ingestionRows, "duplicate") + Count(ingestionRows, "same_path_redetected");
        var skipped = Count(ingestionRows, "skipped_non_media");
        var skippedOrDuplicate = duplicates + skipped;
        var failed = Count(pipelineRows, nameof(IdentityJobState.Failed)) + Count(ingestionRows, "failed") + Count(ingestionRows, "missing");
        var registered = Math.Max(0, displayBatch?.FilesIdentified ?? lifecycle.Identified);
        var review = Math.Max(0, displayBatch is null
            ? lifecycle.InReview
            : displayBatch.FilesReview + displayBatch.FilesNoMatch + displayBatch.FilesFailed);

        var identityTotal = pipelineRows.Values.Sum();
        var retailMatched = SumStates(pipelineRows, RetailMatchedStates);
        var retailReview = SumStates(pipelineRows, RetailReviewStates);
        var retailNoMatch = SumStates(pipelineRows, RetailNoMatchStates);
        var retailFinished = retailMatched + retailReview + retailNoMatch;
        var retailEligible = Math.Max(batchTotal, Math.Max(identityTotal + skippedOrDuplicate, retailFinished + skippedOrDuplicate));

        var wikidataResolved = SumStates(pipelineRows, WikidataResolvedStates);
        var wikidataReview = SumStates(pipelineRows, WikidataReviewStates);
        var wikidataEligible = Math.Max(retailMatched + retailReview, wikidataResolved + wikidataReview);
        var enriched = Math.Min(wikidataEligible, SumStates(pipelineRows, EnrichmentCompleteStates));
        var enrichmentTotal = Math.Max(wikidataEligible, enriched);
        var identified = Math.Min(retailEligible, Math.Max(batchProcessed, retailFinished + skippedOrDuplicate + failed));

        return
        [
            Stage("detected", "Detected", batchProcessed, batchTotal, "Files found and handed to ingestion"),
            Stage("parsed", "Parsed", batchProcessed, batchTotal, "Names and embedded metadata interpreted"),
            Stage("identified", "Identified", identified, retailEligible, "Recognized as media and routed into identity matching"),
            Stage("matched", "Matched", retailMatched, retailEligible, "Matched with retail metadata providers"),
            Stage("retail_review", "Retail Review", retailReview + retailNoMatch, retailEligible, "Retail matches needing review or files with no retail match"),
            Stage("canonicalized", "Canonicalized", wikidataResolved, wikidataEligible, "Linked to canonical identity when available"),
            Stage("wikidata_review", "Wikidata Review", wikidataReview, wikidataEligible, "Wikidata matches needing review or files with no QID"),
            Stage("enriched", "Enriched", enriched, enrichmentTotal, "Artwork, people, series, genres, universes added"),
            Stage("organized", "Organized", registered, batchTotal, "Rename and folder operations completed"),
            Stage("registered", "Registered", registered, batchTotal, "Ready in the library"),
            Stage("needs_review", "Needs Review", review, batchTotal, "Waiting for a curator decision"),
            Stage("duplicate", "Duplicates", duplicates, batchTotal, "Files already tracked or identical to an existing item"),
            Stage("skipped", "Skipped", skipped, batchTotal, "Non-media sidecars ignored"),
            Stage("failed", "Failed", failed, batchTotal, "Could not finish automatically"),
        ];
    }

    private async Task<List<IngestionCurrentActivityDto>> ReadCurrentActivitiesAsync(
        Guid batchId,
        IReadOnlyList<IngestionPipelineStageDto> stages,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<CurrentActivityRow>("""
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    media_type,
                    updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            )
            SELECT
                lj.entity_id AS EntityId,
                lj.state AS State,
                lj.media_type AS MediaType,
                lj.updated_at AS UpdatedAt,
                ma.file_path_root AS SourcePath,
                COALESCE(
                    (
                        SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = lj.entity_id
                          AND cv.key IN ('title', 'episode_title')
                          AND cv.value IS NOT NULL
                          AND cv.value <> ''
                        ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END
                        LIMIT 1
                    ),
                    (
                        SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = w.id
                          AND cv.key = 'title'
                          AND cv.value IS NOT NULL
                          AND cv.value <> ''
                        LIMIT 1
                    ),
                    ''
                ) AS Title
            FROM latest_jobs lj
            LEFT JOIN media_assets ma ON ma.id = lj.entity_id
            LEFT JOIN editions e ON e.id = ma.edition_id
            LEFT JOIN works w ON w.id = e.work_id
            WHERE lj.rn = 1
              AND lj.state IN @states
            ORDER BY
                CASE lj.state
                    WHEN 'RetailSearching' THEN 10
                    WHEN 'BridgeSearching' THEN 20
                    WHEN 'Hydrating' THEN 30
                    WHEN 'UniverseEnriching' THEN 40
                    ELSE 90
                END,
                lj.updated_at DESC
            LIMIT @limit;
            """, new { batchId = batchId.ToString(), states = CurrentActivityStates, limit = 3 });

        return rows
            .Select(row => ToCurrentActivity(row, stages))
            .ToList();
    }

    private async Task<List<IngestionCurrentActivityDto>> ReadTaskActivitiesAsync(
        Guid batchId,
        IReadOnlyList<IngestionPipelineStageDto> stages,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = _db.CreateConnection();
        var rows = (await conn.QueryAsync<CurrentActivityRow>("""
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    media_type,
                    updated_at,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            )
            SELECT
                lj.entity_id AS EntityId,
                lj.state AS State,
                lj.media_type AS MediaType,
                lj.updated_at AS UpdatedAt,
                ma.file_path_root AS SourcePath,
                COALESCE(
                    (
                        SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = lj.entity_id
                          AND cv.key IN ('title', 'episode_title')
                          AND cv.value IS NOT NULL
                          AND cv.value <> ''
                        ORDER BY CASE cv.key WHEN 'title' THEN 0 ELSE 1 END
                        LIMIT 1
                    ),
                    (
                        SELECT cv.value
                        FROM canonical_values cv
                        WHERE cv.entity_id = w.id
                          AND cv.key = 'title'
                          AND cv.value IS NOT NULL
                          AND cv.value <> ''
                        LIMIT 1
                    ),
                    ''
                ) AS Title
            FROM latest_jobs lj
            LEFT JOIN media_assets ma ON ma.id = lj.entity_id
            LEFT JOIN editions e ON e.id = ma.edition_id
            LEFT JOIN works w ON w.id = e.work_id
            WHERE lj.rn = 1
            ORDER BY lj.updated_at DESC;
            """, new { batchId = batchId.ToString() })).AsList();

        if (rows.Count == 0)
        {
            return [];
        }

        var metrics = await conn.QueryFirstOrDefaultAsync<ActivityMetricCounts>("""
            SELECT
                (SELECT COUNT(*) FROM canonical_values WHERE key IN ('cover_url', 'poster_url', 'image', 'thumbnail', 'hero_image') AND value IS NOT NULL AND value <> '') AS ArtworkCount,
                (SELECT COUNT(*) FROM collection_relationships) AS RelationshipCount,
                (SELECT COUNT(DISTINCT person_id) FROM person_media_links) AS PersonCount,
                (SELECT COUNT(*) FROM review_queue WHERE status = 'Pending') AS IssueCount;
            """) ?? new ActivityMetricCounts();

        var result = new List<IngestionCurrentActivityDto>
        {
            BuildTaskActivity(
                "artwork",
                "Fetching artwork",
                "Retrieving covers and posters from providers.",
                "retail",
                rows,
                stages,
                activeStates: [nameof(IdentityJobState.RetailSearching)],
                relevantStates: [
                    nameof(IdentityJobState.Queued),
                    nameof(IdentityJobState.RetailSearching),
                    ..RetailMatchedStates,
                    ..RetailReviewStates,
                    ..RetailNoMatchStates,
                    nameof(IdentityJobState.Failed),
                ],
                completedStates: [..RetailMatchedStates, ..RetailReviewStates, ..RetailNoMatchStates],
                reviewStates: [..RetailReviewStates, ..RetailNoMatchStates, nameof(IdentityJobState.Failed)],
                metricLabel: "Artwork found",
                metricValue: metrics.ArtworkCount.ToString("N0"),
                metricTone: "info"),
            BuildTaskActivity(
                "wikidata",
                "Linking Wikidata QIDs",
                "Matching works to canonical entities.",
                "wikidata",
                rows,
                stages,
                activeStates: [nameof(IdentityJobState.BridgeSearching)],
                relevantStates: [
                    nameof(IdentityJobState.RetailMatched),
                    nameof(IdentityJobState.BridgeSearching),
                    ..WikidataResolvedStates,
                    ..WikidataReviewStates,
                ],
                completedStates: WikidataResolvedStates,
                reviewStates: WikidataReviewStates,
                metricLabel: "QIDs linked",
                metricValue: CountStage(stages.ToDictionary(stage => stage.Key, StringComparer.OrdinalIgnoreCase), "canonicalized").ToString("N0"),
                metricTone: "success"),
            BuildTaskActivity(
                "relationships",
                "Series & relationships",
                "Building series and relationship graphs.",
                "enrichment",
                rows,
                stages,
                activeStates: [nameof(IdentityJobState.UniverseEnriching)],
                relevantStates: [
                    nameof(IdentityJobState.QidResolved),
                    nameof(IdentityJobState.Hydrating),
                    nameof(IdentityJobState.UniverseEnriching),
                    ..EnrichmentCompleteStates,
                ],
                completedStates: EnrichmentCompleteStates,
                reviewStates: [],
                metricLabel: "Links created",
                metricValue: metrics.RelationshipCount.ToString("N0"),
                metricTone: "success"),
            BuildTaskActivity(
                "people",
                "People & cast enrichment",
                "Enriching authors, narrators, cast, and characters.",
                "enrichment",
                rows,
                stages,
                activeStates: [nameof(IdentityJobState.UniverseEnriching)],
                relevantStates: [
                    nameof(IdentityJobState.QidResolved),
                    nameof(IdentityJobState.Hydrating),
                    nameof(IdentityJobState.UniverseEnriching),
                    ..EnrichmentCompleteStates,
                ],
                completedStates: EnrichmentCompleteStates,
                reviewStates: [],
                metricLabel: "People resolved",
                metricValue: metrics.PersonCount.ToString("N0"),
                metricTone: "warning"),
        };

        return result
            .Where(activity => activity.TotalCount > 0 || activity.ActiveCount > 0 || activity.ProcessedCount > 0)
            .ToList();
    }

    private static IngestionCurrentActivityDto BuildTaskActivity(
        string taskKey,
        string message,
        string detail,
        string progressStageKey,
        IReadOnlyList<CurrentActivityRow> rows,
        IReadOnlyList<IngestionPipelineStageDto> stages,
        IReadOnlyCollection<string> activeStates,
        IReadOnlyCollection<string> relevantStates,
        IReadOnlyCollection<string> completedStates,
        IReadOnlyCollection<string> reviewStates,
        string metricLabel,
        string metricValue,
        string metricTone)
    {
        var relevant = rows
            .Where(row => ContainsState(relevantStates, row.State))
            .OrderBy(row => TaskSort(row, activeStates, completedStates, reviewStates))
            .ThenByDescending(row => ParseDate(row.UpdatedAt))
            .ToList();
        var active = relevant.Where(row => IsFreshActive(row, activeStates)).ToList();
        var completed = relevant.Where(row => ContainsState(completedStates, row.State)).ToList();
        var review = relevant.Where(row => ContainsState(reviewStates, row.State)).ToList();
        var progress = ResolveActivityProgress(progressStageKey, stages);
        var total = Math.Max(progress.Total, relevant.Count);
        var processed = Math.Clamp(Math.Max(progress.Count, completed.Count + review.Count), 0, Math.Max(0, total));
        var queued = Math.Max(0, total - processed - active.Count);
        var samples = active.Count > 0 ? active : relevant.Where(row => !ContainsState(completedStates, row.State)).Take(8).ToList();
        if (samples.Count == 0)
        {
            samples = relevant.Take(8).ToList();
        }
        var currentItem = samples.Select(DisplayActivityItem).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return new IngestionCurrentActivityDto
        {
            StageKey = taskKey,
            Message = message,
            Detail = detail,
            CurrentItem = currentItem,
            Source = FirstNonBlank(active.Select(row => ShortPath(row.SourcePath)).FirstOrDefault(), relevant.Select(row => ShortPath(row.SourcePath)).FirstOrDefault()),
            ProcessedCount = processed,
            TotalCount = total,
            PercentComplete = total > 0 ? Math.Clamp(processed * 100d / total, 0, 100) : 0,
            LastUpdatedTime = LatestUpdated(relevant),
            QueuedCount = queued,
            ActiveCount = active.Count,
            SampleItems = samples.Select(DisplayActivityItem).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList(),
            MetricLabel = metricLabel,
            MetricValue = metricValue,
            MetricTone = metricTone,
            CurrentBatch = BuildCurrentBatch(relevant, activeStates, completedStates, reviewStates),
        };
    }

    private static IngestionActivityBatchDto? BuildCurrentBatch(
        IReadOnlyList<CurrentActivityRow> relevant,
        IReadOnlyCollection<string> activeStates,
        IReadOnlyCollection<string> completedStates,
        IReadOnlyCollection<string> reviewStates)
    {
        if (relevant.Count == 0)
        {
            return null;
        }

        var totalBatches = Math.Max(1, (int)Math.Ceiling(relevant.Count / (double)ActivityBatchSize));
        var firstActiveIndex = IndexOf(relevant, row => IsFreshActive(row, activeStates));
        var firstPendingIndex = IndexOf(relevant, row =>
            !IsFreshActive(row, activeStates)
            && !ContainsState(completedStates, row.State)
            && !ContainsState(reviewStates, row.State));
        var startIndex = Math.Max(0, firstActiveIndex >= 0 ? firstActiveIndex : firstPendingIndex >= 0 ? firstPendingIndex : 0);
        var batchNumber = Math.Min(totalBatches, startIndex / ActivityBatchSize + 1);
        var batchRows = relevant
            .Skip((batchNumber - 1) * ActivityBatchSize)
            .Take(ActivityBatchSize)
            .ToList();

        var active = batchRows.Where(row => IsFreshActive(row, activeStates)).ToList();
        var completed = batchRows.Where(row => ContainsState(completedStates, row.State)).ToList();
        var review = batchRows.Where(row => ContainsState(reviewStates, row.State)).ToList();
        var pending = batchRows
            .Where(row => !IsFreshActive(row, activeStates)
                && !ContainsState(completedStates, row.State)
                && !ContainsState(reviewStates, row.State))
            .ToList();

        return new IngestionActivityBatchDto
        {
            BatchNumber = batchNumber,
            BatchSize = batchRows.Count,
            TotalBatches = totalBatches,
            CompletedCount = completed.Count,
            ActiveCount = active.Count,
            PendingCount = pending.Count,
            ReviewCount = review.Count,
            ActiveItems = active.Select(DisplayActivityItem).Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToList(),
            CompletedPreview = completed.Select(DisplayActivityItem).Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToList(),
            PendingPreview = pending.Select(DisplayActivityItem).Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToList(),
            ReviewPreview = review.Select(DisplayActivityItem).Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToList(),
        };
    }

    private static int TaskSort(
        CurrentActivityRow row,
        IReadOnlyCollection<string> activeStates,
        IReadOnlyCollection<string> completedStates,
        IReadOnlyCollection<string> reviewStates)
    {
        if (IsFreshActive(row, activeStates))
        {
            return 0;
        }

        if (!ContainsState(completedStates, row.State) && !ContainsState(reviewStates, row.State))
        {
            return 1;
        }

        return ContainsState(reviewStates, row.State) ? 2 : 3;
    }

    private static bool ContainsState(IReadOnlyCollection<string> states, string? state) =>
        !string.IsNullOrWhiteSpace(state) && states.Contains(state, StringComparer.OrdinalIgnoreCase);

    private static bool IsFreshActive(CurrentActivityRow row, IReadOnlyCollection<string> activeStates)
    {
        if (!ContainsState(activeStates, row.State))
        {
            return false;
        }

        var updated = ParseDate(row.UpdatedAt);
        return updated is null || DateTimeOffset.UtcNow - updated.Value.ToUniversalTime() <= ActiveActivityFreshness;
    }

    private static int IndexOf(IReadOnlyList<CurrentActivityRow> rows, Func<CurrentActivityRow, bool> predicate)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (predicate(rows[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static DateTimeOffset? LatestUpdated(IReadOnlyList<CurrentActivityRow> rows)
    {
        var values = rows
            .Select(row => ParseDate(row.UpdatedAt))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    private static string DisplayActivityItem(CurrentActivityRow row) =>
        FirstNonBlank(row.Title, ShortPath(row.SourcePath), row.MediaType, "Current file");

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

    private async Task<IReadOnlyList<IngestionBatch>> ReconcileCompletedBatchesAsync(
        IReadOnlyList<IngestionBatch> recentBatches,
        CancellationToken ct)
    {
        var changed = false;
        var hasTrackedBatch = recentBatches.Any(batch => batch.FilesProcessed > 0 || HasOutcomeCounters(batch));

        foreach (var batch in recentBatches)
        {
            if (!ActiveBatchStatuses.Contains(batch.Status, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var snapshot = await ReadBatchTerminalSnapshotAsync(batch.Id, ct);
            var identified = Math.Max(batch.FilesIdentified, snapshot.Identified);
            var review = Math.Max(batch.FilesReview, snapshot.Review);
            var noMatch = Math.Max(batch.FilesNoMatch, snapshot.NoMatch);
            var failed = Math.Max(batch.FilesFailed, snapshot.Failed);
            var terminal = identified + review + noMatch + failed;
            var noActivePipelineWork = snapshot.Queued == 0 && snapshot.Active == 0;
            var age = DateTimeOffset.UtcNow - batch.StartedAt.ToUniversalTime();
            var isNoWorkBatch = batch.FilesTotal > 0
                && terminal == 0
                && batch.FilesProcessed == 0
                && snapshot.TotalJobs == 0
                && snapshot.LogRows == 0
                && noActivePipelineWork
                && ((hasTrackedBatch && age > NoWorkBatchGrace) || age > NoWorkBatchMaximumAge);
            var isStaleUntrackedBatch = batch.FilesTotal > 0
                && terminal == 0
                && batch.FilesProcessed == 0
                && snapshot.TotalJobs == 0
                && noActivePipelineWork
                && DateTimeOffset.UtcNow - batch.StartedAt.ToUniversalTime() > TimeSpan.FromMinutes(30);
            var allFilesTerminal = batch.FilesTotal <= 0
                ? noActivePipelineWork
                : terminal >= batch.FilesTotal || (batch.FilesProcessed >= batch.FilesTotal && noActivePipelineWork);

            if ((!allFilesTerminal && !isStaleUntrackedBatch && !isNoWorkBatch) || !noActivePipelineWork)
            {
                continue;
            }

            var processed = isStaleUntrackedBatch || isNoWorkBatch
                ? batch.FilesTotal
                : Math.Max(batch.FilesProcessed, terminal);
            if (batch.FilesTotal > 0)
            {
                processed = Math.Clamp(processed, 0, batch.FilesTotal);
            }

            await _batchRepository.UpdateCountsAsync(
                batch.Id,
                batch.FilesTotal,
                processed,
                identified,
                review,
                noMatch,
                failed,
                ct);

            await _batchRepository.CompleteAsync(
                batch.Id,
                failed > 0 && failed >= Math.Max(1, batch.FilesTotal) ? "failed" : "completed",
                ct);

            changed = true;
        }

        return changed
            ? await _batchRepository.GetRecentAsync(12, ct)
            : recentBatches;
    }

    private async Task<BatchTerminalSnapshot> ReadBatchTerminalSnapshotAsync(Guid batchId, CancellationToken ct)
    {
        using var conn = _db.CreateConnection();
        return await conn.QueryFirstOrDefaultAsync<BatchTerminalSnapshot>(
            """
            WITH latest_jobs AS (
                SELECT
                    entity_id,
                    state,
                    ROW_NUMBER() OVER (
                        PARTITION BY entity_id
                        ORDER BY updated_at DESC, created_at DESC
                    ) AS rn
                FROM identity_jobs
                WHERE ingestion_run_id = @batchId
            ),
            job_states AS (
                SELECT entity_id, state
                FROM latest_jobs
                WHERE rn = 1
            ),
            pending_reviews AS (
                SELECT DISTINCT entity_id
                FROM review_queue
                WHERE status = 'Pending'
            )
            SELECT
                COALESCE(SUM(CASE WHEN js.state IN ('Ready', 'ReadyWithoutUniverse', 'Completed', 'UniverseEnriching') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS Identified,
                COALESCE(SUM(CASE
                    WHEN js.state IN ('QidNeedsReview', 'RetailMatchedNeedsReview') THEN 1
                    WHEN pr.entity_id IS NOT NULL
                         AND js.state IN ('Ready', 'ReadyWithoutUniverse', 'Completed', 'RetailNoMatch', 'QidNoMatch', 'Failed')
                        THEN 1
                    ELSE 0
                END), 0) AS Review,
                COALESCE(SUM(CASE WHEN js.state IN ('RetailNoMatch', 'QidNoMatch') AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS NoMatch,
                COALESCE(SUM(CASE WHEN js.state = 'Failed' AND pr.entity_id IS NULL THEN 1 ELSE 0 END), 0) AS Failed,
                COALESCE(SUM(CASE WHEN js.state = 'Queued' THEN 1 ELSE 0 END), 0) AS Queued,
                COALESCE(SUM(CASE WHEN js.state IN ('RetailSearching', 'RetailMatched', 'RetailMatchedNeedsReview', 'BridgeSearching', 'QidResolved', 'Hydrating', 'UniverseEnriching') THEN 1 ELSE 0 END), 0) AS Active,
                COUNT(js.entity_id) AS TotalJobs,
                (
                    SELECT COUNT(*)
                    FROM ingestion_log il
                    WHERE il.ingestion_run_id = @batchId
                ) AS LogRows
            FROM job_states js
            LEFT JOIN pending_reviews pr ON pr.entity_id = js.entity_id;
            """,
            new { batchId = batchId.ToString() }) ?? new BatchTerminalSnapshot();
    }

    private static IngestionOperationsJobDto ToActiveJob(
        IngestionBatch batch,
        IReadOnlyDictionary<string, int> pipelineRows,
        IReadOnlyList<IngestionPipelineStageDto> stages)
    {
        var currentStage = ResolveBatchStage(batch, pipelineRows, stages);
        var stageKey = ResolveActivityStageKey(currentStage);
        var progress = ResolveActivityProgress(stageKey, stages);
        var total = Math.Max(0, progress.Total > 0 ? progress.Total : batch.FilesTotal);
        var processed = Math.Clamp(Math.Max(0, progress.Total > 0 ? progress.Count : batch.FilesProcessed), 0, total);
        var percent = total > 0
            ? Math.Clamp(processed * 100d / total, 0, 100)
            : 0;

        return new IngestionOperationsJobDto
        {
            JobId = batch.Id,
            JobType = "Ingestion batch",
            MediaType = batch.Category,
            SourceFolder = batch.SourcePath,
            CurrentStage = currentStage,
            CurrentItem = null,
            ProcessedCount = processed,
            TotalCount = total,
            PercentComplete = percent,
            Status = batch.Status,
            Elapsed = DateTimeOffset.UtcNow - batch.StartedAt.ToUniversalTime(),
            LastUpdatedTime = batch.UpdatedAt,
            WarningSummary = batch.FilesFailed > 0 ? $"{batch.FilesFailed:N0} failed" : null,
        };
    }

    private static IngestionCurrentActivityDto ToCurrentActivity(
        CurrentActivityRow row,
        IReadOnlyList<IngestionPipelineStageDto> stages)
    {
        var stageKey = ResolveActivityStageKey(row.State);
        var progress = ResolveActivityProgress(stageKey, stages);
        var currentItem = FirstNonBlank(row.Title, ShortPath(row.SourcePath), row.MediaType, "Current file");
        var total = Math.Max(0, progress.Total);
        var processed = Math.Clamp(Math.Max(0, progress.Count), 0, Math.Max(0, total));

        return new IngestionCurrentActivityDto
        {
            StageKey = stageKey,
            Message = ResolveActivityMessage(row.State),
            Detail = $"Working on {currentItem}",
            CurrentItem = currentItem,
            Source = ShortPath(row.SourcePath),
            ProcessedCount = processed,
            TotalCount = total,
            PercentComplete = total > 0 ? Math.Clamp(processed * 100d / total, 0, 100) : 0,
            LastUpdatedTime = ParseDate(row.UpdatedAt),
        };
    }

    private static IngestionCurrentActivityDto ToCurrentActivity(
        IngestionOperationsJobDto job,
        IReadOnlyList<IngestionPipelineStageDto> stages)
    {
        if (!job.Status.Equals("running", StringComparison.OrdinalIgnoreCase)
            && !job.Status.Equals("processing", StringComparison.OrdinalIgnoreCase)
            && !job.Status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return new IngestionCurrentActivityDto();
        }

        var stageKey = ResolveActivityStageKey(job.CurrentStage);
        var progress = ResolveActivityProgress(stageKey, stages);
        var total = Math.Max(0, progress.Total > 0 ? progress.Total : job.TotalCount);
        var processed = Math.Clamp(Math.Max(0, progress.Total > 0 ? progress.Count : job.ProcessedCount), 0, Math.Max(0, total));
        var item = FirstNonBlank(job.CurrentItem, job.CurrentStage, "Ingestion is running");

        return new IngestionCurrentActivityDto
        {
            StageKey = stageKey,
            Message = ResolveActivityMessage(job.CurrentStage),
            Detail = item,
            CurrentItem = item,
            Source = ShortPath(job.SourceFolder),
            ProcessedCount = processed,
            TotalCount = total,
            PercentComplete = total > 0 ? Math.Clamp(processed * 100d / total, 0, 100) : Math.Clamp(job.PercentComplete, 0, 100),
            LastUpdatedTime = job.LastUpdatedTime,
        };
    }

    private static string ResolveActivityStageKey(string? stateOrStage)
    {
        var value = stateOrStage ?? string.Empty;
        if (value.Contains("Bridge", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Qid", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Wikidata", StringComparison.OrdinalIgnoreCase)
            || value.Contains("canonical", StringComparison.OrdinalIgnoreCase))
        {
            return "wikidata";
        }

        if (value.Contains("Retail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("match", StringComparison.OrdinalIgnoreCase)
            || value.Contains("identify", StringComparison.OrdinalIgnoreCase))
        {
            return "retail";
        }

        if (value.Contains("Hydrat", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Universe", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Enrich", StringComparison.OrdinalIgnoreCase))
        {
            return "enrichment";
        }

        if (value.Contains("scan", StringComparison.OrdinalIgnoreCase)
            || value.Contains("parse", StringComparison.OrdinalIgnoreCase))
        {
            return "scanning";
        }

        return "scanning";
    }

    private static (int Count, int Total) ResolveActivityProgress(
        string stageKey,
        IReadOnlyList<IngestionPipelineStageDto> stages)
    {
        var byKey = stages.ToDictionary(stage => stage.Key, StringComparer.OrdinalIgnoreCase);
        var duplicateOrSkipped = CountStage(byKey, "duplicate") + CountStage(byKey, "skipped");

        return stageKey switch
        {
            "retail" => (
                CountStage(byKey, "matched") + CountStage(byKey, "retail_review") + duplicateOrSkipped,
                TotalStage(byKey, "matched")),
            "wikidata" => (
                CountStage(byKey, "canonicalized") + CountStage(byKey, "wikidata_review"),
                TotalStage(byKey, "canonicalized")),
            "enrichment" => (
                CountStage(byKey, "enriched"),
                TotalStage(byKey, "enriched")),
            _ => (
                CountStage(byKey, "detected"),
                TotalStage(byKey, "detected")),
        };
    }

    private static int CountStage(IReadOnlyDictionary<string, IngestionPipelineStageDto> stages, string key) =>
        stages.TryGetValue(key, out var stage) ? stage.Count : 0;

    private static int TotalStage(IReadOnlyDictionary<string, IngestionPipelineStageDto> stages, string key) =>
        stages.TryGetValue(key, out var stage) ? stage.TotalCount : 0;

    private static string ResolveActivityMessage(string? stateOrStage)
    {
        var value = stateOrStage ?? string.Empty;
        if (value.Contains(nameof(IdentityJobState.RetailSearching), StringComparison.OrdinalIgnoreCase)
            || value.Contains("Retail", StringComparison.OrdinalIgnoreCase)
            || value.Contains("match", StringComparison.OrdinalIgnoreCase)
            || value.Contains("identify", StringComparison.OrdinalIgnoreCase))
        {
            return "Matching metadata";
        }

        if (value.Contains(nameof(IdentityJobState.BridgeSearching), StringComparison.OrdinalIgnoreCase)
            || value.Contains("Wikidata", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Qid", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Bridge", StringComparison.OrdinalIgnoreCase))
        {
            return "Checking Wikidata identity";
        }

        if (value.Contains(nameof(IdentityJobState.Hydrating), StringComparison.OrdinalIgnoreCase))
        {
            return "Adding metadata";
        }

        if (value.Contains(nameof(IdentityJobState.UniverseEnriching), StringComparison.OrdinalIgnoreCase)
            || value.Contains("Enrich", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Universe", StringComparison.OrdinalIgnoreCase))
        {
            return "Enriching relationships";
        }

        if (value.Contains("scan", StringComparison.OrdinalIgnoreCase))
        {
            return "Scanning files";
        }

        return "Processing files";
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

    private static string ResolveBatchStage(
        IngestionBatch batch,
        IReadOnlyDictionary<string, int> pipelineRows,
        IReadOnlyList<IngestionPipelineStageDto> stages)
    {
        var scanningProgress = ResolveActivityProgress("scanning", stages);
        var retailProgress = ResolveActivityProgress("retail", stages);
        var wikidataProgress = ResolveActivityProgress("wikidata", stages);
        var enrichmentProgress = ResolveActivityProgress("enrichment", stages);

        if (IsIncomplete(scanningProgress))
        {
            return "Scanning source folder";
        }

        if (Count(pipelineRows, nameof(IdentityJobState.RetailSearching)) > 0
            || Count(pipelineRows, nameof(IdentityJobState.Queued)) > 0)
        {
            return "Retail identification";
        }

        if (Count(pipelineRows, nameof(IdentityJobState.BridgeSearching)) > 0)
        {
            return "Wikidata matching";
        }

        if (Count(pipelineRows, nameof(IdentityJobState.Hydrating)) > 0
            || Count(pipelineRows, nameof(IdentityJobState.UniverseEnriching)) > 0)
        {
            return "Enrichment";
        }

        if (IsIncomplete(retailProgress))
        {
            return "Retail identification";
        }

        if (IsIncomplete(wikidataProgress))
        {
            return "Wikidata matching";
        }

        if (IsIncomplete(enrichmentProgress))
        {
            return "Enrichment";
        }

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

    private static bool IsIncomplete((int Count, int Total) progress) =>
        progress.Total > 0 && progress.Count < progress.Total;

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

    private static IngestionPipelineStageDto Stage(string key, string label, int count, int totalCount, string helper) =>
        new()
        {
            Key = key,
            Label = label,
            Count = Math.Max(0, count),
            TotalCount = Math.Max(0, totalCount),
            Helper = helper
        };

    private static IngestionReviewReasonDto Review(string key, string label, int count, string explanation) =>
        new() { Key = key, Label = label, Count = Math.Max(0, count), Explanation = explanation };

    private static int Count(IReadOnlyDictionary<string, int> counts, string key) =>
        counts.TryGetValue(key, out var count) ? count : 0;

    private static int SumStates(IReadOnlyDictionary<string, int> counts, IEnumerable<string> states) =>
        states.Sum(state => Count(counts, state));

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

    private sealed class CurrentActivityRow
    {
        public string EntityId { get; set; } = "";
        public string State { get; set; } = "";
        public string? MediaType { get; set; }
        public string? UpdatedAt { get; set; }
        public string? SourcePath { get; set; }
        public string? Title { get; set; }
    }

    private sealed class ActivityMetricCounts
    {
        public int ArtworkCount { get; init; }
        public int RelationshipCount { get; init; }
        public int PersonCount { get; init; }
        public int IssueCount { get; init; }
    }

    private sealed class BatchTerminalSnapshot
    {
        public int Identified { get; init; }
        public int Review { get; init; }
        public int NoMatch { get; init; }
        public int Failed { get; init; }
        public int Queued { get; init; }
        public int Active { get; init; }
        public int TotalJobs { get; init; }
        public int LogRows { get; init; }
    }

    private sealed record BatchSummaryTotals(int Total, int Registered, int Review);

    private sealed record FolderProbe(string Label, bool? IsReachable, bool? PermissionsValid);
}
