using Microsoft.Extensions.Options;
using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
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
            "Reads library.xml sidecars in the Library Root and hydrates the database. " +
            "XML always wins on conflict (Great Inhale).")
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

        return app;
    }
}
