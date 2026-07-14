using MediaEngine.Api.Models;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services.ReadServices;
using MediaEngine.Contracts.Paging;
using MediaEngine.Domain;
using MediaEngine.Domain.Aggregates;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Storage.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Library endpoints for operational health, batch edits, and universe curation.
/// </summary>
public static class LibraryEndpoints
{
    public static IEndpointRouteBuilder MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/library")
                       .WithTags("Library");

        // â”€â”€ GET /library/overview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/overview", async (
            ILibraryItemRepository libraryItemRepo,
            ILibraryOverviewReadService overviewReadService,
            CancellationToken ct) =>
        {
            // 1. Four-state counts (Identified, InReview, Provisional, Rejected) + trigger breakdown
            var fourState = await libraryItemRepo.GetFourStateCountsAsync(ct: ct);

            // 2. Shared projection summary
            var projection = await libraryItemRepo.GetProjectionSummaryAsync(ct);

            // 3. Admin overview media type counts should reflect owned media assets,
            // not catalogue-only works discovered during enrichment.
            var mediaTypeCounts = await libraryItemRepo.GetOwnedMediaTypeCountsAsync(ct);
            var ownedTotal = mediaTypeCounts.Values.Sum();

            // 4. Review-ready count from the shared libraryItem projection
            var reviewTotal = fourState.InReview;

            var overview = await overviewReadService.GetOverviewAggregatesAsync(ct);

            var dto = new LibraryOverviewDto
            {
                TotalItems = ownedTotal,
                Added24h = overview.Added24h,
                Added7d = overview.Added7d,
                Added30d = overview.Added30d,
                PipelineStates = overview.PipelineStates.ToDictionary(kv => kv.Key, kv => kv.Value),
                PipelineSuccessRate = overview.PipelineSuccessRate,
                ReviewCategories = fourState.TriggerCounts.ToDictionary(kv => kv.Key, kv => kv.Value),
                ReviewTotal = reviewTotal,
                WithQid = projection.WithQid,
                WithoutQid = projection.WithoutQid,
                EnrichedStage3 = projection.EnrichedStage3,
                NotEnrichedStage3 = projection.NotEnrichedStage3,
                UniverseAssigned = projection.UniverseAssigned,
                UniverseUnassigned = projection.UniverseUnassigned,
                StaleItems = projection.StaleItems,
                MediaTypeCounts = mediaTypeCounts,
                HiddenByQualityGate = projection.HiddenByQualityGate,
                ArtPending = projection.ArtPending,
                RetailNeedsReview = projection.RetailNeedsReview,
                QidNoMatch = projection.QidNoMatch,
                CompletedWithArt = projection.CompletedWithArt,
            };

            return Results.Ok(dto);
        })
        .WithName("GetLibraryOverview")
        .WithSummary("Aggregated operational health summary for the library dashboard.")
        .Produces<LibraryOverviewDto>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        group.MapGet("/works", async (
            ILibraryWorkFeedReadService workFeedReadService,
            ILoggerFactory loggerFactory,
            int? offset,
            int? limit,
            CancellationToken ct) =>
        {
            ct.ThrowIfCancellationRequested();

            var page = PagedRequest.From(offset, limit, defaultLimit: 100, maxLimit: 500);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var logger = loggerFactory.CreateLogger("MediaEngine.Api.LibraryWorks");
            var response = await workFeedReadService.GetWorksAsync(page, ct);
            sw.Stop();
            if (sw.ElapsedMilliseconds >= 1000)
            {
                logger.LogWarning(
                    "Large-list read {Operation} took {ElapsedMs} ms with offset {Offset}, limit {Limit}, returned {ItemCount}, has_more {HasMore}",
                    "library.works",
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
                    "library.works",
                    sw.ElapsedMilliseconds,
                    response.Offset,
                    response.Limit,
                    response.Items.Count,
                    response.HasMore);
            }

            return Results.Ok(response);
        })
        .WithName("GetLibraryWorks")
        .WithSummary("Returns library-owned works for the home and browse surfaces.")
        .Produces<PagedResponse<LibraryWorkListItemDto>>(StatusCodes.Status200OK)
        .RequireAnyRole();

        // â”€â”€ POST /library/batch-edit/preview â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/batch-edit/preview", async (
            LibraryBatchEditRequest request,
            ICanonicalValueRepository canonicalRepo,
            ILibraryCurationReadService curationReadService,
            CancellationToken ct) =>
        {
            if (request.EntityIds.Count == 0 || request.FieldChanges.Count == 0)
            {
                return Results.BadRequest("Must provide entity IDs and field changes.");
            }

            var targetMap = await curationReadService.ResolveBatchEditTargetsAsync(
                request.EntityIds,
                request.FieldChanges.Select(change => change.Key).ToArray(),
                ct);
            var targetIds = targetMap.Values
                .SelectMany(changeTargets => changeTargets.Values)
                .Distinct()
                .ToList();
            var allCanonicals = await canonicalRepo.GetByEntitiesAsync(targetIds, ct);

            var changes = new List<LibraryFieldChangePreview>();
            foreach (var change in request.FieldChanges)
            {
                var oldValueCounts = new Dictionary<string, int>();
                foreach (var entityId in request.EntityIds)
                {
                    var targetId = targetMap.TryGetValue(entityId, out var targets)
                        && targets.TryGetValue(change.Key, out var resolvedTargetId)
                        ? resolvedTargetId
                        : entityId;
                    var entityCanonicals = allCanonicals.TryGetValue(targetId, out var vals)
                        ? vals
                        : [];
                    var current = entityCanonicals.FirstOrDefault(c => c.Key == change.Key);
                    var oldVal = current?.Value ?? "(empty)";
                    oldValueCounts[oldVal] = oldValueCounts.GetValueOrDefault(oldVal, 0) + 1;
                }
                changes.Add(new LibraryFieldChangePreview
                {
                    Key = change.Key,
                    NewValue = change.Value,
                    OldValueCounts = oldValueCounts,
                });
            }

            return Results.Ok(new LibraryBatchEditPreview
            {
                AffectedCount = request.EntityIds.Count,
                Changes = changes,
            });
        })
        .WithName("PreviewBatchEdit")
        .WithSummary("Dry-run preview of a batch edit operation.")
        .Produces<LibraryBatchEditPreview>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/batch-edit â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/batch-edit", async (
            LibraryBatchEditRequest request,
            ICanonicalValueRepository canonicalRepo,
            IMetadataClaimRepository claimRepo,
            ILibraryCurationReadService curationReadService,
            CancellationToken ct) =>
        {
            if (request.EntityIds.Count == 0 || request.FieldChanges.Count == 0)
            {
                return Results.BadRequest("Must provide entity IDs and field changes.");
            }

            var updatedCount = 0;
            var failedIds = new List<Guid>();
            var errors = new List<string>();
            var targetMap = await curationReadService.ResolveBatchEditTargetsAsync(
                request.EntityIds,
                request.FieldChanges.Select(change => change.Key).ToArray(),
                ct);
            var claimsByTargetAndKey = new Dictionary<(Guid TargetId, string Key), MetadataClaim>();
            var canonicalsByTargetAndKey = new Dictionary<(Guid TargetId, string Key), CanonicalValue>();
            var now = DateTimeOffset.UtcNow;

            foreach (var entityId in request.EntityIds)
            {
                try
                {
                    if (!targetMap.TryGetValue(entityId, out var targets))
                    {
                        failedIds.Add(entityId);
                        errors.Add($"{entityId}: no owned media asset was found for batch edit.");
                        continue;
                    }

                    foreach (var change in request.FieldChanges)
                    {
                        if (!targets.TryGetValue(change.Key, out var targetId))
                        {
                            targetId = entityId;
                        }

                        var targetKey = (targetId, change.Key);

                        // Create one user-locked claim per resolved target/key. If ten selected
                        // tracks share the same album parent, album fields are written once.
                        claimsByTargetAndKey[targetKey] = new MetadataClaim
                        {
                            Id = Guid.NewGuid(),
                            EntityId = targetId,
                            ClaimKey = change.Key,
                            ClaimValue = change.Value,
                            ProviderId = WellKnownProviders.UserManual,
                            Confidence = 1.0,
                            IsUserLocked = true,
                            ClaimedAt = now,
                        };

                        canonicalsByTargetAndKey[targetKey] = new CanonicalValue
                        {
                            EntityId = targetId,
                            Key = change.Key,
                            Value = change.Value,
                            LastScoredAt = now,
                            IsConflicted = false,
                            WinningProviderId = WellKnownProviders.UserManual,
                            NeedsReview = false,
                        };
                    }

                    updatedCount++;
                }
                catch (Exception ex)
                {
                    failedIds.Add(entityId);
                    errors.Add($"{entityId}: {ex.Message}");
                }
            }

            if (claimsByTargetAndKey.Count > 0)
            {
                await claimRepo.InsertBatchAsync(claimsByTargetAndKey.Values.ToList(), ct);
            }

            if (canonicalsByTargetAndKey.Count > 0)
            {
                await canonicalRepo.UpsertBatchAsync(canonicalsByTargetAndKey.Values.ToList(), ct);
            }

            return Results.Ok(new LibraryBatchEditResult
            {
                UpdatedCount = updatedCount,
                FailedIds = failedIds,
                Errors = errors,
            });
        })
        .WithName("ApplyBatchEdit")
        .WithSummary("Apply batch field edits to multiple items.")
        .Produces<LibraryBatchEditResult>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ GET /library/universe-candidates â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/universe-candidates", async (
            ILibraryCurationReadService curationReadService,
            CancellationToken ct) =>
        {
            return Results.Ok(await curationReadService.GetUniverseCandidatesAsync(ct));
        })
        .WithName("GetUniverseCandidates")
        .WithSummary("Items with universe-related QIDs but no collection assignment.")
        .Produces<List<UniverseCandidateDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/{workId}/accept â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/{workId:guid}/accept", async (
            Guid workId,
            UniverseAcceptRequest request,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            // Find or create the collection for the target QID
            var collection = await collectionRepo.FindByQidAsync(request.TargetCollectionQid, ct);
            if (collection is null)
            {
                // Create a new ContentGroup collection for this QID
                collection = new Collection
                {
                    Id = Guid.NewGuid(),
                    WikidataQid = request.TargetCollectionQid,
                    DisplayName = request.TargetCollectionQid, // Will be enriched later
                    CollectionType = "ContentGroup",
                    Resolution = "materialized",
                    Scope = "library",
                    IsEnabled = true,
                };
                await collectionRepo.UpsertAsync(collection, ct);
            }

            await collectionRepo.AssignWorkToCollectionAsync(workId, collection.Id, ct);
            return Results.Ok(new { assigned = true, collection_id = collection.Id });
        })
        .WithName("AcceptUniverseCandidate")
        .WithSummary("Accept a universe assignment for a work.")
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/{workId}/reject â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/{workId:guid}/reject", async (
            Guid workId,
            ICanonicalValueRepository canonicalRepo,
            ILibraryCurationReadService curationReadService,
            CancellationToken ct) =>
        {
            var entityId = await curationReadService.FindOwnedAssetIdForWorkAsync(workId, ct);

            if (entityId is null)
            {
                return Results.NotFound();
            }

            // Mark as reviewed/rejected so it doesn't reappear in the pending queue
            await canonicalRepo.UpsertBatchAsync([new CanonicalValue
            {
                EntityId = entityId.Value,
                Key = "universe_review_status",
                Value = "rejected",
                LastScoredAt = DateTimeOffset.UtcNow,
                IsConflicted = false,
                WinningProviderId = WellKnownProviders.UserManual,
                NeedsReview = false,
            }], ct);

            return Results.Ok(new { rejected = true });
        })
        .WithName("RejectUniverseCandidate")
        .WithSummary("Reject a universe candidate for a work.")
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-candidates/batch-accept â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-candidates/batch-accept", async (
            UniverseBatchAcceptRequest request,
            ICollectionRepository collectionRepo,
            ILibraryCurationReadService curationReadService,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var accepted = 0;
            var failedIds = new List<Guid>();
            var missingCandidateIds = new List<Guid>();
            var errors = new List<string>();
            var logger = loggerFactory.CreateLogger("MediaEngine.Api.LibraryUniverseCuration");
            var candidateQids = await curationReadService.GetBestUniverseCandidateQidsAsync(
                request.WorkIds,
                ct);

            foreach (var workId in request.WorkIds.Distinct())
            {
                try
                {
                    if (!candidateQids.TryGetValue(workId, out var candidateQid))
                    {
                        missingCandidateIds.Add(workId);
                        continue;
                    }

                    var collection = await collectionRepo.FindByQidAsync(candidateQid, ct);
                    if (collection is null)
                    {
                        collection = new Collection
                        {
                            Id = Guid.NewGuid(),
                            WikidataQid = candidateQid,
                            DisplayName = candidateQid,
                            CollectionType = "ContentGroup",
                            Resolution = "materialized",
                            Scope = "library",
                            IsEnabled = true,
                        };
                        await collectionRepo.UpsertAsync(collection, ct);
                    }

                    await collectionRepo.AssignWorkToCollectionAsync(workId, collection.Id, ct);
                    accepted++;
                }
                catch (Exception ex)
                {
                    failedIds.Add(workId);
                    errors.Add($"{workId}: {ex.Message}");
                    logger.LogError(ex, "Failed to accept universe candidate for work {WorkId}.", workId);
                }
            }

            return Results.Ok(new UniverseBatchAcceptResult
            {
                AcceptedCount = accepted,
                MissingCandidateIds = missingCandidateIds,
                FailedIds = failedIds,
                Errors = errors,
            });
        })
        .WithName("BatchAcceptUniverseCandidates")
        .WithSummary("Batch accept universe assignments.")
        .RequireAdminOrCurator();

        // â”€â”€ GET /library/universe-unlinked â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapGet("/universe-unlinked", async (
            ILibraryCurationReadService curationReadService,
            CancellationToken ct) =>
        {
            return Results.Ok(await curationReadService.GetUniverseUnlinkedAsync(ct));
        })
        .WithName("GetUniverseUnlinked")
        .WithSummary("Works with Wikidata QID but no universe-related properties.")
        .Produces<List<UnlinkedWorkDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // â”€â”€ POST /library/universe-assign â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        group.MapPost("/universe-assign", async (
            UniverseManualAssignRequest request,
            ICollectionRepository collectionRepo,
            CancellationToken ct) =>
        {
            await collectionRepo.AssignWorkToCollectionAsync(request.WorkId, request.CollectionId, ct);
            return Results.Ok(new { assigned = true });
        })
        .WithName("ManualUniverseAssign")
        .WithSummary("Manually assign a work to an existing collection.")
        .RequireAdminOrCurator();

        return app;
    }
}


