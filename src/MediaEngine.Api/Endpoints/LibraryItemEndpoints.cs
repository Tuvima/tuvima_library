using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Domain;
using MediaEngine.Domain.Constants;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;
using MediaEngine.Domain.Services;
using MediaEngine.Storage.Contracts;
using MediaEngine.Storage.Services;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Unified curator-facing view of ingested media items. Endpoint handlers own HTTP
/// mapping while persistence is delegated to typed repositories and curation services.
/// </summary>
public static class LibraryItemEndpoints
{
    public static IEndpointRouteBuilder MapLibraryItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library/items")
            .WithTags("LibraryItems");

        group.MapGet("", async (
            int? offset,
            int? limit,
            string? search,
            string? type,
            string? status,
            double? minConfidence,
            string? matchSource,
            bool? duplicatesOnly,
            string? sort,
            int? maxDays,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var query = new LibraryItemQuery(
                Offset: offset ?? 0,
                Limit: limit ?? 50,
                Search: search,
                MediaType: type,
                Status: status,
                MinConfidence: minConfidence,
                MatchSource: matchSource,
                DuplicatesOnly: duplicatesOnly ?? false,
                Sort: sort,
                MaxDays: maxDays);

            return Results.Ok(await repo.GetPageAsync(query, ct));
        })
        .WithName("GetLibraryCatalogItems")
        .WithSummary("Paginated list of all ingested items with filtering.")
        .Produces<LibraryItemsPage>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapGet("/{entityId}/detail", async (
            Guid entityId,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
        {
            var detail = await repo.GetDetailAsync(entityId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        })
        .WithName("GetLibraryItemDetail")
        .WithSummary("Full detail for a single library item.")
        .Produces<LibraryItemDetail>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapGet("/counts", async (ILibraryItemRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetStatusCountsAsync(ct)))
        .WithName("GetLibraryItemStatusCounts")
        .WithSummary("Status counts for tab badges (All, Staging, Review, Auto, Edited, Duplicate).")
        .Produces<LibraryItemStatusCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapGet("/state-counts", async (
            Guid? batchId,
            ILibraryItemRepository repo,
            CancellationToken ct) =>
            Results.Ok(await repo.GetFourStateCountsAsync(batchId, ct)))
        .WithName("GetLibraryItemLifecycleCounts")
        .WithSummary("Four-state counts (Registered, NeedsReview, NoMatch, Failed) with trigger breakdown.")
        .Produces<LibraryItemLifecycleCounts>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapGet("/type-counts", async (ILibraryItemRepository repo, CancellationToken ct) =>
            Results.Ok(await repo.GetMediaTypeCountsAsync(ct)))
        .WithName("GetLibraryItemTypeCounts")
        .WithSummary("Per-media-type item counts.")
        .Produces<Dictionary<string, int>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId}/apply-match", async (
            Guid entityId,
            ApplyMatchRequest request,
            IMetadataClaimRepository claimRepo,
            IHydrationPipelineService pipeline,
            ICollectionRepository collectionRepo,
            ILibraryItemCurationStore store,
            ISystemActivityRepository activityRepo,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var target = await store.ResolveTargetAsync(entityId, ct);
            if (target is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var now = DateTimeOffset.UtcNow;
            var claims = BuildApplyMatchClaims(target.AssetId, request, now);
            var mediaType = Enum.TryParse<MediaType>(target.MediaType, true, out var parsedMediaType)
                ? parsedMediaType
                : MediaType.Unknown;

            if (claims.Count > 0)
            {
                await claimRepo.InsertBatchAsync(claims, ct);
                await store.UpsertCanonicalValuesAsync(target.AssetId, claims, ct);
            }

            var hydrationTriggered = false;
            var wikidataStatus = string.IsNullOrWhiteSpace(request.Qid)
                ? WorkWikidataStatus.Missing
                : WorkWikidataStatus.Confirmed;
            await collectionRepo.UpdateWorkWikidataStatusAsync(target.WorkId, wikidataStatus, ct);
            await store.MarkWorkRegisteredAsync(target.WorkId, ct);

            try
            {
                if (!string.IsNullOrWhiteSpace(request.Qid))
                {
                    var hints = BuildHydrationHints(request);
                    await pipeline.RunSynchronousAsync(new HarvestRequest
                    {
                        EntityId = target.AssetId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = mediaType,
                        Hints = hints,
                        Pass = HydrationPass.Universe,
                        SuppressReviewCreation = true,
                    }, ct);
                    hydrationTriggered = true;
                }
                else
                {
                    var pipelineResult = await pipeline.RunSynchronousAsync(new HarvestRequest
                    {
                        EntityId = target.AssetId,
                        EntityType = EntityType.MediaAsset,
                        MediaType = mediaType,
                        SkipRetailStage = true,
                        SuppressReviewCreation = true,
                    }, ct);
                    hydrationTriggered = true;
                    if (!string.IsNullOrWhiteSpace(pipelineResult.WikidataQid))
                    {
                        wikidataStatus = WorkWikidataStatus.Confirmed;
                        await collectionRepo.UpdateWorkWikidataStatusAsync(target.WorkId, wikidataStatus, ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Curator match for {EntityId} was saved, but follow-up hydration failed",
                    entityId);
            }

            await store.CompletePendingReviewsAsync(
                target.AssetId,
                target.WorkId,
                "Resolved",
                "user:curator",
                now,
                ct);

            var displayTitle = request.Title ?? target.Title ?? "unknown";
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                OccurredAt = now,
                ActionType = SystemActionType.ReviewItemResolved,
                CollectionName = displayTitle,
                EntityId = target.WorkId,
                EntityType = "Work",
                Detail = wikidataStatus == WorkWikidataStatus.Confirmed
                    ? $"Registered '{displayTitle}' - QID {request.Qid ?? "resolved during hydration"} confirmed."
                    : $"Match applied for '{displayTitle}' - retail metadata only (no Wikidata QID).",
            }, ct);

            return Results.Ok(new ApplyMatchResponse
            {
                EntityId = entityId,
                WikidataStatus = wikidataStatus,
                ClaimsWritten = claims.Count,
                HydrationTriggered = hydrationTriggered,
                Message = wikidataStatus == WorkWikidataStatus.Confirmed
                    ? $"Registered '{displayTitle}' with canonical identity."
                    : "Match applied. Metadata claims written.",
            });
        })
        .WithName("ApplyLibraryItemMatch")
        .WithSummary("Apply a selected match to a library item. Provide a QID to register the item.")
        .Produces<ApplyMatchResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId}/create-manual", async (
            Guid entityId,
            CreateManualRequest request,
            IMetadataClaimRepository claimRepo,
            ICollectionRepository collectionRepo,
            ILibraryItemCurationStore store,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.BadRequest("Title is required for manual entry.");

            var target = await store.ResolveTargetAsync(entityId, ct);
            if (target is null)
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var claims = BuildManualClaims(target.AssetId, request, DateTimeOffset.UtcNow);
            await claimRepo.InsertBatchAsync(claims, ct);
            await store.UpsertCanonicalValuesAsync(target.AssetId, claims, ct);
            await collectionRepo.UpdateWorkWikidataStatusAsync(target.WorkId, WorkWikidataStatus.Manual, ct);

            return Results.Ok(new CreateManualResponse
            {
                EntityId = entityId,
                WikidataStatus = WorkWikidataStatus.Manual,
                ClaimsWritten = claims.Count,
                Message = $"Manual entry created with {claims.Count} user-locked fields. Item marked for future Universe sweep.",
            });
        })
        .WithName("CreateManualLibraryItemEntry")
        .WithSummary("Create a manual metadata entry for a library item with no provider match.")
        .Produces<CreateManualResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapDelete("/{entityId}", async (
            Guid entityId,
            ILibraryItemCurationStore store,
            WorkHierarchyMaintenanceService hierarchyMaintenance,
            ISystemActivityRepository activityRepo,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var targets = await store.GetRemovalTargetsAsync([entityId], ct);
            if (!targets.TryGetValue(entityId, out var target) || target.FilePaths.Count == 0)
                return Results.NotFound($"No media assets found for work {entityId}.");

            var filesDeleted = await DeleteTargetAsync(
                target, store, hierarchyMaintenance, activityRepo, logger, isBatch: false, ct);

            return Results.Ok(new
            {
                EntityId = entityId,
                FilesDeleted = filesDeleted,
                Message = $"Removed '{target.Title ?? "unknown"}' and {filesDeleted} file(s).",
            });
        })
        .WithName("DeleteLibraryCatalogItem")
        .WithSummary("Permanently remove a work and all its files from the library.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId}/reject", async (
            Guid entityId,
            ILibraryItemCurationStore store,
            ISystemActivityRepository activityRepo,
            IConfigurationLoader configLoader,
            IEventPublisher publisher,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var target = await store.ResolveTargetAsync(entityId, ct);
            if (target is null || string.IsNullOrWhiteSpace(target.FilePath))
                return Results.NotFound($"No current media asset or work target found for {entityId}.");

            var rejectedDirectory = ResolveRejectedDirectory(configLoader);
            if (rejectedDirectory is null)
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            Directory.CreateDirectory(rejectedDirectory);
            try
            {
                var newPath = await RejectTargetAsync(
                    target, rejectedDirectory, store, activityRepo, publisher, logger, ct);
                return Results.Ok(new
                {
                    EntityId = entityId,
                    NewFilePath = newPath,
                    Message = $"Rejected '{target.Title ?? "unknown"}'.",
                });
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not reject library item {EntityId}", entityId);
                return Results.Problem($"Could not move file to rejected folder: {ex.Message}");
            }
        })
        .WithName("RejectLibraryCatalogItem")
        .WithSummary("Reject a library item: move its file to .data/staging/rejected and mark it as Rejected.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/batch/approve", async (
            BatchLibraryItemRequest request,
            ILibraryItemCurationStore store,
            CancellationToken ct) =>
        {
            var entityIds = DistinctEntityIds(request);
            if (entityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            var processed = await store.ApproveWorksAsync(entityIds, DateTimeOffset.UtcNow, ct);
            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount = processed,
                TotalRequested = entityIds.Length,
                Message = $"Approved {processed} of {entityIds.Length} items.",
            });
        })
        .WithName("BatchApproveLibraryCatalogItems")
        .WithSummary("Approve multiple library items in one transaction.")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapPost("/batch/delete", async (
            BatchLibraryItemRequest request,
            ILibraryItemCurationStore store,
            WorkHierarchyMaintenanceService hierarchyMaintenance,
            ISystemActivityRepository activityRepo,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var entityIds = DistinctEntityIds(request);
            if (entityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            var targets = await store.GetRemovalTargetsAsync(entityIds, ct);
            var processed = 0;
            var filesDeleted = 0;
            foreach (var entityId in entityIds)
            {
                if (!targets.TryGetValue(entityId, out var target) || target.FilePaths.Count == 0)
                    continue;

                try
                {
                    filesDeleted += await DeleteTargetAsync(
                        target, store, hierarchyMaintenance, activityRepo, logger, isBatch: true, ct);
                    processed++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Batch delete failed for work {WorkId}", entityId);
                }
            }

            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount = processed,
                TotalRequested = entityIds.Length,
                Message = $"Deleted {processed} of {entityIds.Length} items ({filesDeleted} files removed).",
            });
        })
        .WithName("BatchDeleteLibraryCatalogItems")
        .WithSummary("Permanently delete multiple library items and their files in batch.")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapPost("/batch/reject", async (
            BatchLibraryItemRequest request,
            ILibraryItemCurationStore store,
            ISystemActivityRepository activityRepo,
            IConfigurationLoader configLoader,
            IEventPublisher publisher,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var entityIds = DistinctEntityIds(request);
            if (entityIds.Length == 0)
                return Results.BadRequest("No entity IDs provided.");

            var rejectedDirectory = ResolveRejectedDirectory(configLoader);
            if (rejectedDirectory is null)
                return Results.BadRequest("LibraryRoot is not configured. Cannot determine rejected folder.");

            Directory.CreateDirectory(rejectedDirectory);
            var targets = await store.ResolveWorkTargetsAsync(entityIds, ct);
            var processed = 0;
            foreach (var entityId in entityIds)
            {
                if (!targets.TryGetValue(entityId, out var target) || string.IsNullOrWhiteSpace(target.FilePath))
                    continue;

                try
                {
                    await RejectTargetAsync(
                        target, rejectedDirectory, store, activityRepo, publisher, logger, ct);
                    processed++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Batch rejection failed for work {WorkId}", entityId);
                }
            }

            return Results.Ok(new BatchLibraryItemResponse
            {
                ProcessedCount = processed,
                TotalRequested = entityIds.Length,
                Message = $"Rejected {processed} of {entityIds.Length} items.",
            });
        })
        .WithName("BatchRejectLibraryCatalogItems")
        .WithSummary("Reject multiple library items and move their representative files to the rejected folder.")
        .Produces<BatchLibraryItemResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/recover", async (
            Guid entityId,
            ILibraryItemCurationStore store,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            var recovered = await store.RecoverAsync(entityId, DateTimeOffset.UtcNow, ct);
            if (recovered is null)
                return Results.NotFound($"Work {entityId} is not in rejected state.");

            await LogSupplementaryActivityAsync(activityRepo, new SystemActivityEntry
            {
                ActionType = SystemActionType.ItemUnrejected,
                EntityId = entityId,
                EntityType = "Work",
                Detail = "Un-rejected - returned to review queue for re-evaluation.",
            }, logger, ct);

            await PublishSupplementaryAsync(publisher, SignalREvents.ReviewItemCreated, new
            {
                review_item_id = recovered.ReviewId ?? Guid.Empty,
                entity_id = entityId,
                trigger = "UserFixMatch",
            }, logger, ct);

            return Results.Ok(new { message = "Item un-rejected and returned to review queue." });
        })
        .WithName("RecoverLibraryCatalogItem")
        .WithSummary("Recover a previously rejected library item and return it to review.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapPost("/{entityId:guid}/provisional", async (
            Guid entityId,
            ProvisionalMetadataRequest body,
            ILibraryItemCurationStore store,
            ISystemActivityRepository activityRepo,
            IEventPublisher publisher,
            ILogger<LibraryItemEndpointLog> logger,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest("Title is required for provisional metadata.");

            var provisional = await store.MarkProvisionalAsync(entityId, body, DateTimeOffset.UtcNow, ct);
            if (provisional is null)
                return Results.NotFound($"Work {entityId} not found.");

            await LogSupplementaryActivityAsync(activityRepo, new SystemActivityEntry
            {
                ActionType = SystemActionType.ItemProvisional,
                EntityId = entityId,
                EntityType = "Work",
                CollectionName = body.Title,
                Detail = $"Marked '{body.Title}' as provisional with curator-entered metadata.",
            }, logger, ct);

            await PublishSupplementaryAsync(publisher, SignalREvents.ReviewItemResolved, new
            {
                entity_id = entityId,
                action = "provisional",
            }, logger, ct);

            return Results.Ok(new
            {
                EntityId = entityId,
                State = "Provisional",
                Title = body.Title,
                Message = $"'{body.Title}' marked as provisional.",
            });
        })
        .WithName("MarkProvisional")
        .WithSummary("Mark a library item as provisional with curator-entered metadata.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        group.MapGet("/{entityId:guid}/history", async (
            Guid entityId,
            ILibraryItemCurationStore store,
            CancellationToken ct) =>
            Results.Ok(await store.GetHistoryAsync(entityId, ct)))
        .WithName("GetLibraryCatalogItemHistory")
        .WithSummary("Get processing history timeline for a library item.")
        .Produces<IReadOnlyList<LibraryItemHistoryEntry>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        return app;
    }

    private static List<MetadataClaim> BuildApplyMatchClaims(
        Guid assetId,
        ApplyMatchRequest request,
        DateTimeOffset now)
    {
        var claims = new List<MetadataClaim>();
        AddUserClaim(claims, assetId, MetadataFieldConstants.Title, request.Title, now);
        AddUserClaim(claims, assetId, "release_year", request.Year, now);
        AddUserClaim(claims, assetId, MetadataFieldConstants.Author, request.Author, now);
        AddUserClaim(claims, assetId, "director", request.Director, now);
        AddUserClaim(claims, assetId, MetadataFieldConstants.Description, request.Description, now);
        AddUserClaim(claims, assetId, MetadataFieldConstants.CoverUrl, request.CoverUrl, now);
        AddUserClaim(claims, assetId, BridgeIdKeys.WikidataQid, request.Qid, now);
        return claims;
    }

    private static List<MetadataClaim> BuildManualClaims(
        Guid assetId,
        CreateManualRequest request,
        DateTimeOffset now)
    {
        var claims = new List<MetadataClaim>();
        AddUserClaim(claims, assetId, MetadataFieldConstants.Title, request.Title, now);
        AddUserClaim(claims, assetId, "release_year", request.Year, now);
        AddUserClaim(claims, assetId, MetadataFieldConstants.Author, request.Author, now);
        AddUserClaim(claims, assetId, MetadataFieldConstants.Description, request.Description, now);
        return claims;
    }

    private static void AddUserClaim(
        ICollection<MetadataClaim> claims,
        Guid entityId,
        string key,
        string? value,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        claims.Add(new MetadataClaim
        {
            Id = Guid.NewGuid(),
            EntityId = entityId,
            ProviderId = WellKnownProviders.UserManual,
            ClaimKey = key,
            ClaimValue = value.Trim(),
            Confidence = 1.0,
            ClaimedAt = now,
            IsUserLocked = true,
        });
    }

    private static Dictionary<string, string> BuildHydrationHints(ApplyMatchRequest request)
    {
        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddHint(hints, BridgeIdKeys.WikidataQid, request.Qid);
        AddHint(hints, MetadataFieldConstants.Title, request.Title);
        AddHint(hints, "release_year", request.Year);
        AddHint(hints, MetadataFieldConstants.Author, request.Author);
        return hints;
    }

    private static void AddHint(IDictionary<string, string> hints, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            hints[key] = value.Trim();
    }

    private static Guid[] DistinctEntityIds(BatchLibraryItemRequest request) =>
        request.EntityIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

    private static string? ResolveRejectedDirectory(IConfigurationLoader configLoader)
    {
        var libraryRoot = configLoader.LoadCore().LibraryRoot;
        return string.IsNullOrWhiteSpace(libraryRoot)
            ? null
            : Path.Combine(libraryRoot, ".data", "staging", "rejected");
    }

    private static async Task<int> DeleteTargetAsync(
        LibraryItemRemovalTarget target,
        ILibraryItemCurationStore store,
        WorkHierarchyMaintenanceService hierarchyMaintenance,
        ISystemActivityRepository activityRepo,
        ILogger logger,
        bool isBatch,
        CancellationToken ct)
    {
        var filesDeleted = 0;
        foreach (var filePath in target.FilePaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    filesDeleted++;
                }

                TryPruneEmptyDirectory(Path.GetDirectoryName(filePath), logger);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Could not delete media file {FilePath}; database cleanup will continue", filePath);
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogWarning(ex, "Access denied while deleting media file {FilePath}; database cleanup will continue", filePath);
            }
        }

        foreach (var path in target.ManagedAssetPaths)
            TryDeleteManagedAssetFile(path, logger);

        await store.DeleteWorkRecordsAsync(target, ct);

        try
        {
            await hierarchyMaintenance.CleanupEmptyParentsAsync(target.ParentWorkId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Work {WorkId} was deleted, but empty parent cleanup failed", target.WorkId);
        }

        await LogSupplementaryActivityAsync(activityRepo, new SystemActivityEntry
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActionType = SystemActionType.MediaRemoved,
            CollectionName = target.Title,
            EntityId = target.WorkId,
            EntityType = "Work",
            Detail = $"{(isBatch ? "Batch removed" : "Removed")} '{target.Title ?? "unknown"}' - {filesDeleted} file(s) deleted.",
        }, logger, ct);

        return filesDeleted;
    }

    private static async Task<string> RejectTargetAsync(
        LibraryItemTarget target,
        string rejectedDirectory,
        ILibraryItemCurationStore store,
        ISystemActivityRepository activityRepo,
        IEventPublisher publisher,
        ILogger logger,
        CancellationToken ct)
    {
        var currentPath = target.FilePath!;
        var fileName = Path.GetFileName(currentPath);
        var newPath = Path.Combine(rejectedDirectory, fileName);
        if (File.Exists(newPath) && !string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase))
        {
            newPath = Path.Combine(
                rejectedDirectory,
                $"{Path.GetFileNameWithoutExtension(fileName)}__{target.AssetId}{Path.GetExtension(fileName)}");
        }

        var moved = false;
        if (!string.Equals(currentPath, newPath, StringComparison.OrdinalIgnoreCase) && File.Exists(currentPath))
        {
            File.Move(currentPath, newPath, overwrite: false);
            moved = true;
        }

        try
        {
            await store.MarkRejectedAsync(target, newPath, DateTimeOffset.UtcNow, ct);
        }
        catch
        {
            if (moved)
                TryRestoreMovedFile(newPath, currentPath, logger);
            throw;
        }

        await LogSupplementaryActivityAsync(activityRepo, new SystemActivityEntry
        {
            OccurredAt = DateTimeOffset.UtcNow,
            ActionType = SystemActionType.FileRejected,
            CollectionName = target.Title,
            EntityId = target.WorkId,
            EntityType = "Work",
            Detail = $"Rejected '{target.Title ?? "unknown"}' - file moved to .data/staging/rejected/.",
        }, logger, ct);

        await PublishSupplementaryAsync(publisher, SignalREvents.ReviewItemResolved, new
        {
            entity_id = target.WorkId,
            action = "rejected",
        }, logger, ct);

        return newPath;
    }

    private static async Task LogSupplementaryActivityAsync(
        ISystemActivityRepository activityRepo,
        SystemActivityEntry entry,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await activityRepo.LogAsync(entry, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Primary library item operation succeeded, but activity logging failed");
        }
    }

    private static async Task PublishSupplementaryAsync<T>(
        IEventPublisher publisher,
        string eventName,
        T payload,
        ILogger logger,
        CancellationToken ct)
        where T : notnull
    {
        try
        {
            await publisher.PublishAsync(eventName, payload, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Primary library item operation succeeded, but publishing {EventName} failed",
                eventName);
        }
    }

    private static void TryRestoreMovedFile(string movedPath, string originalPath, ILogger logger)
    {
        try
        {
            if (File.Exists(movedPath) && !File.Exists(originalPath))
                File.Move(movedPath, originalPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex,
                "Database update failed and rejected file could not be restored from {MovedPath} to {OriginalPath}",
                movedPath,
                originalPath);
        }
    }

    private static void TryPruneEmptyDirectory(string? directory, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Could not prune empty media directory {Directory}", directory);
        }
    }

    private static void TryDeleteManagedAssetFile(string path, ILogger logger)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!IsManagedAssetPath(fullPath))
                return;

            if (File.Exists(fullPath))
                File.Delete(fullPath);

            PruneEmptyManagedAssetParents(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogDebug(ex, "Managed artwork cleanup failed for {Path}", path);
        }
    }

    private static bool IsManagedAssetPath(string fullPath) =>
        fullPath.Replace('\\', '/').Contains("/.data/assets/", StringComparison.OrdinalIgnoreCase);

    private static void PruneEmptyManagedAssetParents(string fullPath)
    {
        var current = Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrWhiteSpace(current)
               && IsManagedAssetPath(current)
               && !string.Equals(Path.GetFileName(current), "assets", StringComparison.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                return;

            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }
}

public sealed class LibraryItemEndpointLog;
