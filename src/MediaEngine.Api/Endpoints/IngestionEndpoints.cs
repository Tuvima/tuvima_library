using System.Text.Json.Serialization;
using MediaEngine.Api.Http;
using MediaEngine.Application.ReadModels;
using MediaEngine.Application.Services;
using MediaEngine.Contracts.Ingestion;
using MediaEngine.Contracts.Paging;
using Microsoft.Extensions.Options;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
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

        group.MapGet("/operations", async (
            IIngestionOperationsStatusService statusService,
            CancellationToken ct) =>
        {
            var snapshot = await statusService.GetSnapshotAsync(ct);
            return Results.Ok(snapshot);
        })
        .WithName("GetIngestionOperationsSnapshot")
        .WithSummary("Aggregated Ingestion status for scans, review, providers, folders, and recent batches.")
        .Produces<IngestionOperationsSnapshotDto>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapPost("/scan", async (
            ScanRequest? request,
            IIngestionEngine engine,
            IOptions<IngestionOptions> opts,
            CancellationToken ct) =>
        {
            var rootPath = request?.RootPath
                ?? opts.Value.EffectiveWatchDirectories.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(rootPath))
                return ApiErrors.BadRequest(
                    "No root_path provided and no library source path is configured.");

            if (!Directory.Exists(rootPath))
                return ApiErrors.BadRequest($"Directory does not exist: {rootPath}");

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
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── POST /ingestion/library-scan ──────────────────────────────────────────

        group.MapPost("/library-scan", async (
            ILibraryScanner            scanner,
            IOptions<IngestionOptions> opts,
            CancellationToken          ct) =>
        {
            var root = opts.Value.LibraryRoot;

            if (string.IsNullOrWhiteSpace(root))
                return ApiErrors.BadRequest(
                    "LibraryRoot is not configured. Set Ingestion:LibraryRoot in appsettings.json.");

            if (!Directory.Exists(root))
                return ApiErrors.BadRequest($"Library root does not exist: {root}");

            var result = await scanner.ScanAsync(root, ct);

            // Scan .universe/ to recover fictional entities and relationships.
            var universeResult = await scanner.ScanUniversesAsync(root, ct);

            return Results.Ok(new LibraryScanResponse
            {
                CollectionsUpserted          = result.CollectionsUpserted,
                EditionsUpserted      = result.EditionsUpserted,
                PeopleRecovered       = 0,
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
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdmin();

        // ── GET /ingestion/watch-folder ─────────────────────────────────────────

        group.MapGet("/watch-folder", (
            IOptions<IngestionOptions> opts,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            var page = PagedRequest.From(offset, limit, defaultLimit: 100, maxLimit: 500);
            var watchDir = opts.Value.EffectiveWatchDirectories.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(watchDir))
                return Results.Ok(new WatchFolderPageResponse(null, Array.Empty<WatchFolderFileDto>(), page.Offset, page.Limit, false, null));

            if (!Directory.Exists(watchDir))
                return Results.Ok(new WatchFolderPageResponse(watchDir, Array.Empty<WatchFolderFileDto>(), page.Offset, page.Limit, false, null));

            var searchOption = opts.Value.IncludeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = GetNewestWatchFiles(watchDir, searchOption, page.Offset + page.Limit + 1, ct);
            var response = PagedResponse<WatchFolderFileDto>.FromPage(
                files.Skip(page.Offset).ToList(),
                page);

            return Results.Ok(new WatchFolderPageResponse(
                watchDir,
                response.Items,
                response.Offset,
                response.Limit,
                response.HasMore,
                response.NextCursor));
        })
        .WithName("ListWatchFolder")
        .WithSummary("List files currently sitting in the Watch Folder.")
        .Produces<WatchFolderPageResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── POST /ingestion/rescan ──────────────────────────────────────────────

        group.MapPost("/rescan", async (
            RescanRequest? request,
            IIngestionEngine           engine,
            IOptions<IngestionOptions> opts,
            CancellationToken ct) =>
        {
            var includeSubdirectories = request?.IncludeSubdirectories ?? opts.Value.IncludeSubdirectories;
            var requestedRoot = request?.RootPath;

            if (!string.IsNullOrWhiteSpace(requestedRoot))
            {
                if (!Directory.Exists(requestedRoot))
                    return ApiErrors.BadRequest($"Watch directory does not exist: {requestedRoot}");

                await engine.ScanDirectory(requestedRoot, includeSubdirectories, ct);

                return Results.Accepted(value: new RescanAcceptedResponse(
                    "Rescan triggered. Files will be processed shortly.",
                    1));
            }

            var watchDirs = opts.Value.EffectiveWatchDirectories;
            if (watchDirs.Count == 0)
                return ApiErrors.BadRequest(
                    "No library source paths are configured.");

            var scanTargets = watchDirs
                .Where(Directory.Exists)
                .Select(watchDir => new IngestionScanTarget(watchDir, includeSubdirectories))
                .ToList();

            if (scanTargets.Count == 0)
                return ApiErrors.BadRequest("No configured watch directories exist on disk.");

            await engine.ScanDirectories(scanTargets, ct);

            return Results.Accepted(value: new RescanAcceptedResponse(
                "Rescan triggered. Files will be processed shortly.",
                scanTargets.Count));
        })
        .WithName("TriggerRescan")
        .WithSummary(
            "Re-scan the Watch Folder for new or unprocessed files. " +
            "Files are fed into the ingestion pipeline for processing.")
        .Produces<RescanAcceptedResponse>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /ingestion/reconcile ─────────────────────────────────────────

        group.MapPost("/reconcile", async (
            LibraryReconciliationService reconciler,
            CancellationToken ct) =>
        {
            var result = await reconciler.ReconcileAsync(ct);
            return Results.Ok(new ReconciliationResultResponse(
                result.TotalScanned,
                result.MissingCount,
                result.ElapsedMs));
        })
        .WithName("TriggerReconciliation")
        .WithSummary(
            "Scan all Normal-status assets and clean up any whose files " +
            "are missing from disk.")
        .Produces<ReconciliationResultResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /ingestion/batches ────────────────────────────────────────────
        group.MapGet("/batches", async (
            IIngestionBatchResponseService batchResponses,
            int? limit,
            CancellationToken ct) =>
        {
            var responses = await batchResponses.GetRecentAsync(PagedRequest.From(null, limit, defaultLimit: 20).Limit, ct);
            return Results.Ok(responses);
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
            return Results.Ok(new BatchAttentionCountResponse(count));
        })
        .WithName("GetBatchAttentionCount")
        .WithSummary("Count of items across all batches that need curator attention.")
        .Produces<BatchAttentionCountResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /ingestion/batches/{id} ───────────────────────────────────────
        group.MapGet("/batches/{id:guid}/items", async (
            Guid id,
            IIngestionBatchReadService batchReadService,
            int? offset,
            int? limit,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var page = PagedRequest.From(offset, limit, defaultLimit: 100, maxLimit: 500);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var items = await batchReadService.GetItemsAsync(id, page.Offset, page.Limit + 1, ct);
            var response = PagedResponse<IngestionBatchItemResponse>.FromPage(items, page);
            var logger = loggerFactory.CreateLogger("MediaEngine.Api.IngestionBatches");
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 1000)
            {
                logger.LogWarning(
                    "Large-list read {Operation} took {ElapsedMs} ms with offset {Offset}, limit {Limit}, returned {ItemCount}, has_more {HasMore}",
                    "ingestion.batch.items",
                    sw.ElapsedMilliseconds,
                    response.Offset,
                    response.Limit,
                    response.Items.Count,
                    response.HasMore);
            }
            else
            {
                logger.LogDebug(
                    "Large-list read {Operation} took {ElapsedMs} ms with offset {Offset}, limit {Limit}, returned {ItemCount}, has_more {HasMore}",
                    "ingestion.batch.items",
                    sw.ElapsedMilliseconds,
                    response.Offset,
                    response.Limit,
                    response.Items.Count,
                    response.HasMore);
            }

            return Results.Ok(response);
        })
        .WithName("GetBatchItems")
        .WithSummary("List item-level ingestion progress for a batch.")
        .Produces<List<IngestionBatchItemResponse>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();
        group.MapGet("/batches/{id:guid}", async (
            Guid id,
            IIngestionBatchResponseService batchResponses,
            CancellationToken ct) =>
        {
            var response = await batchResponses.GetByIdAsync(id, ct);
            return response is null ? ApiErrors.NotFound($"Batch '{id}' not found.") : Results.Ok(response);
        })
        .WithName("GetBatchById")
        .WithSummary("Get details of a specific ingestion batch.")
        .Produces<IngestionBatchResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /ingestion/upload ────────────────────────────────────────────────

        group.MapPost("/upload", async (
            HttpRequest request,
            IConfigurationLoader configLoader,
            IOptions<IngestionOptions> opts,
            CancellationToken ct) =>
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            var mediaType = form["mediaType"].ToString();

            if (file is null || string.IsNullOrWhiteSpace(mediaType))
                return ApiErrors.BadRequest("File and mediaType are required.");

            if (file.Length <= 0)
                return ApiErrors.BadRequest("Upload file must not be empty.");

            var libraries = configLoader.LoadLibraries();
            var mediaTypes = configLoader.LoadMediaTypes();
            var watchFolder = libraries.Libraries
                .SelectMany(l => l.SourcePaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .FirstOrDefault(Directory.Exists);

            if (watchFolder is null)
                return ApiErrors.BadRequest("No watch folder configured.");

            var plan = UploadSafety.CreatePlan(
                watchFolder,
                mediaType,
                file.FileName,
                file.Length,
                mediaTypes.Types,
                opts.Value);

            if (plan.Error is not null)
                return plan.Error;

            Directory.CreateDirectory(plan.TargetDirectory);

            if (!UploadSafety.HasRequiredFreeSpace(plan.TargetDirectory, file.Length, opts.Value.UploadFreeSpaceBufferBytes))
            {
                return Results.Problem(
                    title: "Insufficient disk space",
                    detail: "The destination drive does not have enough free space for this upload and the configured safety buffer.",
                    statusCode: StatusCodes.Status507InsufficientStorage);
            }

            var tempPath = Path.Combine(
                plan.TargetDirectory,
                $".{Path.GetFileNameWithoutExtension(plan.SafeFileName)}.{Guid.NewGuid():N}.uploading");

            try
            {
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1024 * 1024,
                    useAsync: true))
                {
                    await file.CopyToAsync(stream, ct);
                }

                File.Move(tempPath, plan.TargetPath);
            }
            catch
            {
                TryDeleteTempUpload(tempPath);
                throw;
            }

            return Results.Ok(new UploadMediaResponse(plan.TargetPath, plan.CanonicalMediaType));
        })
        .WithName("UploadMedia")
        .WithSummary("Upload a media file and route it to the correct watch subfolder.")
        .DisableAntiforgery()
        .Produces<UploadMediaResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static List<WatchFolderFileDto> GetNewestWatchFiles(
        string watchDir,
        SearchOption searchOption,
        int limit,
        CancellationToken ct)
    {
        var bounded = new List<WatchFolderFileDto>(Math.Max(1, limit));
        foreach (var fullPath in Directory.EnumerateFiles(watchDir, "*", searchOption))
        {
            ct.ThrowIfCancellationRequested();

            var info = new FileInfo(fullPath);
            bounded.Add(new WatchFolderFileDto
            {
                FileName      = info.Name,
                RelativePath  = Path.GetRelativePath(watchDir, fullPath),
                FileSizeBytes = info.Length,
                LastModified  = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            });

            if (bounded.Count <= limit)
                continue;

            bounded.Sort(static (left, right) => right.LastModified.CompareTo(left.LastModified));
            bounded.RemoveRange(limit, bounded.Count - limit);
        }

        bounded.Sort(static (left, right) => right.LastModified.CompareTo(left.LastModified));
        return bounded;
    }

    private static void TryDeleteTempUpload(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup. The temp suffix prevents partial files being treated as complete uploads.
        }
    }
}

public static class UploadSafety
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static UploadPlan CreatePlan(
        string watchRoot,
        string mediaType,
        string fileName,
        long fileLength,
        IReadOnlyList<MediaEngine.Storage.Models.MediaTypeDefinition> mediaTypes,
        IngestionOptions options)
    {
        if (string.IsNullOrWhiteSpace(watchRoot))
            return UploadPlan.Fail(ApiErrors.BadRequest("No watch folder configured."));

        if (fileLength <= 0)
            return UploadPlan.Fail(ApiErrors.BadRequest("Upload file must not be empty."));

        if (fileLength > options.MaxUploadSizeBytes)
        {
            return UploadPlan.Fail(Results.Problem(
                title: "Upload too large",
                detail: $"The upload is {fileLength} bytes, which exceeds the configured limit of {options.MaxUploadSizeBytes} bytes.",
                statusCode: StatusCodes.Status413PayloadTooLarge));
        }

        var definition = ResolveMediaType(mediaType, mediaTypes);
        if (definition is null)
            return UploadPlan.Fail(ApiErrors.BadRequest($"Unsupported media type: {mediaType}"));

        if (!IsSafeFileName(fileName, out var safeFileName))
            return UploadPlan.Fail(ApiErrors.BadRequest("Invalid filename."));

        var extension = Path.GetExtension(safeFileName);
        if (string.IsNullOrWhiteSpace(extension)
            || !definition.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return UploadPlan.Fail(ApiErrors.BadRequest(
                $"Files with extension '{extension}' are not allowed for {definition.DisplayName}."));
        }

        var targetDir = Path.GetFullPath(Path.Combine(watchRoot, definition.DisplayName));
        var watchRootFull = Path.GetFullPath(watchRoot);
        var rootPrefix = watchRootFull.EndsWith(Path.DirectorySeparatorChar)
            ? watchRootFull
            : watchRootFull + Path.DirectorySeparatorChar;
        if (!targetDir.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return UploadPlan.Fail(ApiErrors.BadRequest("Invalid media type destination."));

        var targetPath = ResolveCollisionPath(targetDir, safeFileName);
        return new UploadPlan(
            true,
            null,
            targetDir,
            targetPath,
            safeFileName,
            definition.DisplayName);
    }

    public static bool HasRequiredFreeSpace(string targetDirectory, long fileLength, long freeSpaceBufferBytes)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(targetDirectory));
        if (string.IsNullOrWhiteSpace(root))
            return false;

        var drive = new DriveInfo(root);
        return drive.AvailableFreeSpace >= fileLength + Math.Max(0, freeSpaceBufferBytes);
    }

    private static MediaEngine.Storage.Models.MediaTypeDefinition? ResolveMediaType(
        string mediaType,
        IReadOnlyList<MediaEngine.Storage.Models.MediaTypeDefinition> mediaTypes)
    {
        if (string.IsNullOrWhiteSpace(mediaType))
            return null;

        return mediaTypes.FirstOrDefault(t =>
            string.Equals(t.DisplayName, mediaType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(t.Key, mediaType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafeFileName(string fileName, out string safeFileName)
    {
        safeFileName = Path.GetFileName(fileName);
        return !string.IsNullOrWhiteSpace(fileName)
               && string.Equals(fileName, safeFileName, StringComparison.Ordinal)
               && !safeFileName.Any(c => InvalidFileNameChars.Contains(c));
    }

    private static string ResolveCollisionPath(string targetDir, string safeFileName)
    {
        var targetPath = Path.Combine(targetDir, safeFileName);
        var counter = 1;
        var baseName = Path.GetFileNameWithoutExtension(safeFileName);
        var ext = Path.GetExtension(safeFileName);
        while (File.Exists(targetPath))
        {
            targetPath = Path.Combine(targetDir, $"{baseName} ({counter}){ext}");
            counter++;
        }

        return targetPath;
    }

}

internal static class IngestionBatchEndpointMapper
{
    internal static bool ShouldShowInRecentBatches(IngestionBatch batch)
    {
        var hasOutcome = batch.FilesIdentified > 0
            || batch.FilesReview > 0
            || batch.FilesNoMatch > 0
            || batch.FilesFailed > 0;

        return hasOutcome || !string.Equals(batch.Status, "completed", StringComparison.OrdinalIgnoreCase);
    }

}

public sealed record UploadPlan(
    bool IsValid,
    IResult? Error,
    string TargetDirectory,
    string TargetPath,
    string SafeFileName,
    string CanonicalMediaType)
{
    public static UploadPlan Fail(IResult error) => new(false, error, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// Api-internal wire shape for <c>GET /ingestion/watch-folder</c>. Kept endpoint-local (not moved
/// to <c>MediaEngine.Contracts.Ingestion</c>) because it embeds <see cref="WatchFolderFileDto"/>,
/// which lives in <c>MediaEngine.Api.Models</c> — a type the Contracts project cannot reference.
///
/// Property names are deliberately identical to the three anonymous objects this record replaces
/// (no <see cref="JsonPropertyNameAttribute"/>), so the wire shape is unchanged. The two
/// empty-result branches used the implicit-name projection <c>page.Offset, page.Limit</c>
/// (PascalCase members); the populated branch explicitly named them <c>offset</c>/<c>limit</c>
/// (lowercase). Both serialize identically today because the Engine's minimal-API JSON options
/// are the untouched <see cref="System.Text.Json.JsonSerializerDefaults.Web"/> default
/// (camelCase policy, case-insensitive), which folds "Offset"/"Limit" down to "offset"/"limit" —
/// so a single lowercase shape reproduces both call sites byte for byte.
/// </summary>
public sealed record WatchFolderPageResponse(
    string? watch_directory,
    IReadOnlyList<WatchFolderFileDto> files,
    int offset,
    int limit,
    bool has_more,
    string? next_cursor);

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
