using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Ingestion.Contracts;
using MediaEngine.Ingestion.Models;

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
                HubsUpserted          = result.HubsUpserted,
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

        return app;
    }
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
