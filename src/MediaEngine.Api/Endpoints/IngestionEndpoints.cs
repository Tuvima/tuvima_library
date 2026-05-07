using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

public static class IngestionEndpoints
{
    public static IEndpointRouteBuilder MapIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ingestion")
                       .WithTags("Ingestion");

        group.MapPost("/scan", async (
            ScanRequest? request,
            IIngestionEngine engine,
            IOptions<IngestionOptions> opts,
            CancellationToken ct) =>
        {
            var rootPath = request?.RootPath
                ?? opts.Value.WatchDirectory;

            if (string.IsNullOrWhiteSpace(rootPath))
                return Results.BadRequest(
                    "No root_path provided and Ingestion:WatchDirectory is not configured.");

            if (!Directory.Exists(rootPath))
                return Results.BadRequest($"Directory does not exist: {rootPath}");

            var operations = await engine.DryRunAsync(rootPath, ct);
            var response = new ScanResponse
            {
                Operations = operations
                    .Select(PendingOperationDto.FromDomain)
                    .ToList(),
            };

            return Results.Ok(response);
        })
        .WithName("TriggerScan")
        .WithSummary("Simulate a library scan and return pending operations without mutating files.")
        .Produces<ScanResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /ingestion/library-scan ──────────────────────────────────────────

        group.MapPost("/library-scan", async (
            ILibraryScanner            scanner,
            IOptions<IngestionOptions> opts,
            CancellationToken          ct) =>
        {
            var root = opts.Value.LibraryRoot;

            if (string.IsNullOrWhiteSpace(root))
                return Results.BadRequest(
                    "LibraryRoot is not configured. Set Ingestion:LibraryRoot in appsettings.json.");

            if (!Directory.Exists(root))
                return Results.BadRequest($"Library root does not exist: {root}");

            var result = await scanner.ScanAsync(root, ct);

            // Also scan .people/ to recover person records from person.xml sidecars.
            var peopleRecovered = await scanner.ScanPeopleAsync(root, ct);

            // Scan .universe/ to recover fictional entities and relationships.
            var universeResult = await scanner.ScanUniversesAsync(root, ct);

            return Results.Ok(new LibraryScanResponse
            {
                CollectionsUpserted          = result.CollectionsUpserted,
                EditionsUpserted      = result.EditionsUpserted,
                PeopleRecovered       = peopleRecovered,
                UniversesUpserted     = universeResult.UniversesUpserted,
                EntitiesUpserted      = universeResult.EntitiesUpserted,
                RelationshipsUpserted = universeResult.RelationshipsUpserted,
                Errors                = result.Errors + universeResult.Errors,
                ElapsedMs             = (long)result.Elapsed.TotalMilliseconds,
            });
        })
        .WithName("TriggerLibraryScan")
        .WithSummary(
            "Scans media files in the Library Root, updates file paths for known assets, " +
            "and notes new files for a follow-up ingestion pass (Great Inhale v2).")
        .Produces<LibraryScanResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /ingestion/watch-folder ─────────────────────────────────────────

        group.MapGet("/watch-folder", (
            IOptions<IngestionOptions> opts) =>
        {
            var watchDir = opts.Value.WatchDirectory;

            if (string.IsNullOrWhiteSpace(watchDir))
                return Results.Ok(new WatchFolderResponse { Files = [] });

            if (!Directory.Exists(watchDir))
                return Results.Ok(new WatchFolderResponse { Files = [] });

            var searchOption = opts.Value.IncludeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.EnumerateFiles(watchDir, "*", searchOption)
                .Select(fullPath =>
                {
                    var info = new FileInfo(fullPath);
                    return new WatchFolderFileDto
                    {
                        FileName      = info.Name,
                        RelativePath  = Path.GetRelativePath(watchDir, fullPath),
                        FileSizeBytes = info.Length,
                        LastModified  = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                    };
                })
                .OrderByDescending(f => f.LastModified)
                .ToList();

            return Results.Ok(new WatchFolderResponse
            {
                WatchDirectory = watchDir,
                Files          = files,
            });
        })
        .WithName("ListWatchFolder")
        .WithSummary("List files currently sitting in the Watch Folder.")
        .Produces<WatchFolderResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /ingestion/rescan ──────────────────────────────────────────────

        group.MapPost("/rescan", (
            IIngestionEngine           engine,
            IOptions<IngestionOptions> opts) =>
        {
            var watchDir = opts.Value.WatchDirectory;

            if (string.IsNullOrWhiteSpace(watchDir))
                return Results.BadRequest(
                    "Watch directory is not configured. Set Ingestion:WatchDirectory first.");

            if (!Directory.Exists(watchDir))
                return Results.BadRequest($"Watch directory does not exist: {watchDir}");

            engine.ScanDirectory(watchDir, opts.Value.IncludeSubdirectories);

            return Results.Accepted(value: new { message = "Rescan triggered. Files will be processed shortly." });
        })
        .WithName("TriggerRescan")
        .WithSummary(
            "Re-scan the Watch Folder for new or unprocessed files. " +
            "Files are fed into the ingestion pipeline for processing.")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /ingestion/reconcile ─────────────────────────────────────────

        group.MapPost("/reconcile", async (
            LibraryReconciliationService reconciler,
            CancellationToken ct) =>
        {
            var result = await reconciler.ReconcileAsync(ct);
            return Results.Ok(new
            {
                total_scanned = result.TotalScanned,
                missing_count = result.MissingCount,
                elapsed_ms    = result.ElapsedMs,
            });
        })
        .WithName("TriggerReconciliation")
        .WithSummary(
            "Scan all Normal-status assets and clean up any whose files " +
            "are missing from disk.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ingestion/batches ────────────────────────────────────────────
        group.MapGet("/batches", async (
            IIngestionBatchRepository batchRepo,
            int? limit) =>
        {
            var batches = await batchRepo.GetRecentAsync(limit ?? 20);
            return Results.Ok(batches.Select(b => new IngestionBatchResponse
            {
                Id              = b.Id,
                Status          = b.Status,
                SourcePath      = b.SourcePath,
                Category        = b.Category,
                FilesTotal      = b.FilesTotal,
                FilesProcessed  = b.FilesProcessed,
                FilesIdentified = b.FilesIdentified,
                FilesReview     = b.FilesReview,
                FilesNoMatch    = b.FilesNoMatch,
                FilesFailed     = b.FilesFailed,
                StartedAt       = b.StartedAt,
                CompletedAt     = b.CompletedAt,
                CreatedAt       = b.CreatedAt,
            }).ToList());
        })
        .WithName("GetRecentBatches")
        .WithSummary("List recent ingestion batches, newest first.")
        .Produces<List<IngestionBatchResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /ingestion/batches/attention-count ────────────────────────────
        group.MapGet("/batches/attention-count", async (
            IIngestionBatchRepository batchRepo) =>
        {
            var count = await batchRepo.GetNeedsAttentionCountAsync();
            return Results.Ok(new { count });
        })
        .WithName("GetBatchAttentionCount")
        .WithSummary("Count of items across all batches that need curator attention.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /ingestion/batches/{id} ───────────────────────────────────────
        group.MapGet("/batches/{id:guid}/items", (
            Guid id,
            IDatabaseConnection db,
            int? limit,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();
            using var conn = db.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH latest_jobs AS (
                    SELECT
                        entity_id,
                        state,
                        updated_at,
                        ROW_NUMBER() OVER (
                            PARTITION BY entity_id
                            ORDER BY updated_at DESC, created_at DESC
                        ) AS rn
                    FROM identity_jobs
                    WHERE ingestion_run_id = @batchId
                ),
                person_counts AS (
                    SELECT
                        pml.media_asset_id AS entity_id,
                        COUNT(DISTINCT p.id) AS total_people,
                        COUNT(DISTINCT CASE WHEN p.enriched_at IS NOT NULL THEN p.id END) AS enriched_people
                    FROM person_media_links pml
                    INNER JOIN persons p ON p.id = pml.person_id
                    GROUP BY pml.media_asset_id
                ),
                canonical_flags AS (
                    SELECT
                        entity_id,
                        MAX(CASE WHEN key = 'stage3_enriched_at' THEN 1 ELSE 0 END) AS stage3_core_done,
                        MAX(CASE WHEN key = 'stage3_enhanced_at' THEN 1 ELSE 0 END) AS stage3_enhancers_done
                    FROM canonical_values
                    WHERE key IN ('stage3_enriched_at', 'stage3_enhanced_at')
                    GROUP BY entity_id
                )
                SELECT
                    il.id,
                    il.file_path,
                    il.media_asset_id,
                    il.content_hash,
                    il.status,
                    il.media_type,
                    il.confidence_score,
                    il.detected_title,
                    il.error_detail,
                    il.created_at,
                    il.updated_at,
                    lj.state AS identity_state,
                    COALESCE(pc.total_people, 0) AS total_people,
                    COALESCE(pc.enriched_people, 0) AS enriched_people,
                    COALESCE(cf.stage3_core_done, 0) AS stage3_core_done,
                    COALESCE(cf.stage3_enhancers_done, 0) AS stage3_enhancers_done
                FROM ingestion_log il
                LEFT JOIN latest_jobs lj
                    ON lj.entity_id = COALESCE(il.media_asset_id, il.id)
                   AND lj.rn = 1
                LEFT JOIN person_counts pc
                    ON pc.entity_id = COALESCE(il.media_asset_id, il.id)
                LEFT JOIN canonical_flags cf
                    ON cf.entity_id = COALESCE(il.media_asset_id, il.id)
                WHERE il.ingestion_run_id = @batchId
                ORDER BY il.created_at ASC
                LIMIT @limit;
                """;
            cmd.Parameters.AddWithValue("@batchId", id.ToString());
            cmd.Parameters.AddWithValue("@limit", Math.Clamp(limit ?? 500, 1, 5000));

            using var reader = cmd.ExecuteReader();
            var items = new List<IngestionBatchItemResponse>();
            while (reader.Read())
            {
                var filePath = reader.GetString(1);
                var status = reader.IsDBNull(4) ? "detected" : reader.GetString(4);
                var identityState = reader.IsDBNull(11) ? null : reader.GetString(11);
                var stage = ResolveItemStage(status, identityState);
                var totalPeople = reader.GetInt32(12);
                var enrichedPeople = reader.GetInt32(13);
                var stage3CoreDone = reader.GetInt32(14) == 1;
                var stage3EnhancersDone = reader.GetInt32(15) == 1;
                var progressPercent = ResolveItemProgressPercent(
                    stage,
                    identityState,
                    totalPeople,
                    enrichedPeople,
                    stage3CoreDone,
                    stage3EnhancersDone);

                items.Add(new IngestionBatchItemResponse
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    MediaAssetId = reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    ContentHash = reader.IsDBNull(3) ? null : reader.GetString(3),
                    Status = status,
                    MediaType = reader.IsDBNull(5) ? null : reader.GetString(5),
                    ConfidenceScore = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                    DetectedTitle = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ErrorDetail = reader.IsDBNull(8) ? null : reader.GetString(8),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(9)),
                    UpdatedAt = DateTimeOffset.Parse(reader.GetString(10)),
                    IdentityState = identityState,
                    Stage = stage,
                    StageOrder = ResolveItemStageOrder(stage),
                    ProgressPercent = progressPercent,
                    WorkUnitsTotal = ResolveItemWorkUnitsTotal(identityState, totalPeople),
                    WorkUnitsCompleted = ResolveItemWorkUnitsCompleted(identityState, totalPeople, enrichedPeople, stage3CoreDone, stage3EnhancersDone),
                    IsTerminal = progressPercent >= 100,
                });
            }

            return Results.Ok(items);
        })
        .WithName("GetBatchItems")
        .WithSummary("List item-level ingestion progress for a batch.")
        .Produces<List<IngestionBatchItemResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();
        group.MapGet("/batches/{id:guid}", async (
            Guid id,
            IIngestionBatchRepository batchRepo) =>
        {
            var batch = await batchRepo.GetByIdAsync(id);
            if (batch is null) return Results.NotFound();
            return Results.Ok(new IngestionBatchResponse
            {
                Id              = batch.Id,
                Status          = batch.Status,
                SourcePath      = batch.SourcePath,
                Category        = batch.Category,
                FilesTotal      = batch.FilesTotal,
                FilesProcessed  = batch.FilesProcessed,
                FilesIdentified = batch.FilesIdentified,
                FilesReview     = batch.FilesReview,
                FilesNoMatch    = batch.FilesNoMatch,
                FilesFailed     = batch.FilesFailed,
                StartedAt       = batch.StartedAt,
                CompletedAt     = batch.CompletedAt,
                CreatedAt       = batch.CreatedAt,
            });
        })
        .WithName("GetBatchById")
        .WithSummary("Get details of a specific ingestion batch.")
        .Produces<IngestionBatchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /ingestion/upload ────────────────────────────────────────────────

        group.MapPost("/upload", async (
            HttpRequest request,
            IConfigurationLoader configLoader,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            var mediaType = form["mediaType"].ToString();

            if (file is null || string.IsNullOrWhiteSpace(mediaType))
                return Results.BadRequest("File and mediaType are required.");

            var libraries = configLoader.LoadLibraries();
            var watchFolder = libraries.Libraries
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l.SourcePath)
                                     && Directory.Exists(l.SourcePath));

            if (watchFolder is null)
                return Results.BadRequest("No watch folder configured.");

            var targetDir = Path.Combine(watchFolder.SourcePath, mediaType);
            Directory.CreateDirectory(targetDir);

            // Prevent path traversal: reject filenames that contain directory separators
            // or attempt to navigate outside the target directory.
            var safeFileName = Path.GetFileName(file.FileName);
            if (string.IsNullOrWhiteSpace(safeFileName))
                return Results.BadRequest("Invalid filename.");

            var targetPath = Path.Combine(targetDir, safeFileName);

            // Handle filename collision with a counter suffix.
            var counter  = 1;
            var baseName = Path.GetFileNameWithoutExtension(safeFileName);
            var ext      = Path.GetExtension(safeFileName);
            while (File.Exists(targetPath))
            {
                targetPath = Path.Combine(targetDir, $"{baseName} ({counter}){ext}");
                counter++;
            }

            await using var stream = File.Create(targetPath);
            await file.CopyToAsync(stream, ct);

            return Results.Ok(new { path = targetPath, mediaType });
        })
        .WithName("UploadMedia")
        .WithSummary("Upload a media file and route it to the correct watch subfolder.")
        .DisableAntiforgery()
        .RequireAnyRole();

        return app;
    }

    private static string ResolveItemStage(string status, string? identityState)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            return "failed";
        if (string.Equals(status, "needs_review", StringComparison.OrdinalIgnoreCase))
            return "needs_review";

        return identityState switch
        {
            "Queued" => "queued_identity",
            "RetailSearching" or "RetailMatched" or "RetailMatchedNeedsReview" => "identifying",
            "BridgeSearching" or "QidResolved" or "Hydrating" => "hydrating",
            "UniverseEnriching" => "universe_enriching",
            "Ready" or "ReadyWithoutUniverse" or "Completed" => "complete",
            "RetailNoMatch" or "QidNoMatch" or "QidNeedsReview" => "needs_review",
            "Failed" => "failed",
            _ => status,
        };
    }

    private static int ResolveItemStageOrder(string stage) => stage switch
    {
        "detected" => 0,
        "hashing" => 1,
        "processed" => 2,
        "scored" => 3,
        "registered" => 4,
        "queued_identity" => 5,
        "identifying" => 6,
        "hydrating" => 7,
        "universe_enriching" => 8,
        "complete" or "needs_review" or "failed" => 9,
        _ => 0,
    };

    private static int ResolveItemProgressPercent(
        string stage,
        string? identityState,
        int totalPeople,
        int enrichedPeople,
        bool stage3CoreDone,
        bool stage3EnhancersDone)
    {
        if (string.Equals(identityState, "Hydrating", StringComparison.OrdinalIgnoreCase))
        {
            var personProgress = totalPeople <= 0
                ? 1.0
                : Math.Clamp(enrichedPeople / (double)totalPeople, 0, 1);
            return (int)Math.Round(50 + (20 * personProgress));
        }

        if (string.Equals(identityState, "UniverseEnriching", StringComparison.OrdinalIgnoreCase))
        {
            var progress = 80;
            if (stage3CoreDone) progress += 10;
            if (stage3EnhancersDone) progress += 10;
            return progress;
        }

        return stage switch
    {
        "detected" => 5,
        "hashing" => 15,
        "processed" => 35,
        "scored" => 55,
        "registered" => 70,
        "queued_identity" => 10,
        "identifying" => 30,
        "hydrating" => 50,
        "universe_enriching" => 80,
        "complete" or "needs_review" or "failed" => 100,
        _ => 0,
    };
    }

    private static int ResolveItemWorkUnitsTotal(string? identityState, int totalPeople) =>
        identityState switch
        {
            "Hydrating" => Math.Max(totalPeople, 1),
            "UniverseEnriching" => 2,
            _ => 1,
        };

    private static int ResolveItemWorkUnitsCompleted(
        string? identityState,
        int totalPeople,
        int enrichedPeople,
        bool stage3CoreDone,
        bool stage3EnhancersDone) =>
        identityState switch
        {
            "Hydrating" => totalPeople <= 0 ? 1 : Math.Clamp(enrichedPeople, 0, totalPeople),
            "UniverseEnriching" => (stage3CoreDone ? 1 : 0) + (stage3EnhancersDone ? 1 : 0),
            "Ready" or "ReadyWithoutUniverse" or "Completed" or "RetailNoMatch" or "QidNoMatch" or "QidNeedsReview" or "Failed" => 1,
            _ => 0,
        };
}

/// <summary>API response shape for an ingestion batch.</summary>
public sealed class IngestionBatchResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("source_path")]
    public string? SourcePath { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("files_total")]
    public int FilesTotal { get; init; }

    [JsonPropertyName("files_processed")]
    public int FilesProcessed { get; init; }

    [JsonPropertyName("files_identified")]
    public int FilesIdentified { get; init; }

    [JsonPropertyName("files_review")]
    public int FilesReview { get; init; }

    [JsonPropertyName("files_no_match")]
    public int FilesNoMatch { get; init; }

    [JsonPropertyName("files_failed")]
    public int FilesFailed { get; init; }

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>API response shape for one media item inside an ingestion batch.</summary>
public sealed class IngestionBatchItemResponse
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; init; } = "";

    [JsonPropertyName("file_name")]
    public string FileName { get; init; } = "";

    [JsonPropertyName("media_asset_id")]
    public Guid? MediaAssetId { get; init; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("identity_state")]
    public string? IdentityState { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("stage_order")]
    public int StageOrder { get; init; }

    [JsonPropertyName("progress_percent")]
    public int ProgressPercent { get; init; }

    [JsonPropertyName("work_units_total")]
    public int WorkUnitsTotal { get; init; }

    [JsonPropertyName("work_units_completed")]
    public int WorkUnitsCompleted { get; init; }

    [JsonPropertyName("is_terminal")]
    public bool IsTerminal { get; init; }

    [JsonPropertyName("media_type")]
    public string? MediaType { get; init; }

    [JsonPropertyName("confidence_score")]
    public double? ConfidenceScore { get; init; }

    [JsonPropertyName("detected_title")]
    public string? DetectedTitle { get; init; }

    [JsonPropertyName("error_detail")]
    public string? ErrorDetail { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; init; }
}
