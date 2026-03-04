using Tanaste.Api.Models;
using Tanaste.Api.Security;
using Tanaste.Domain.Contracts;
using Tanaste.Domain.Entities;
using Tanaste.Domain.Enums;
using Tanaste.Domain.Models;
using Tanaste.Storage.Contracts;

namespace Tanaste.Api.Endpoints;

/// <summary>
/// Review queue API endpoints for managing ambiguous or low-confidence
/// hydration results that require user intervention.
///
/// Spec: Sprint 4 — Review Queue Endpoints.
/// </summary>
public static class ReviewEndpoints
{
    /// <summary>Well-known provider GUID for user-manual metadata corrections.</summary>
    private static readonly Guid UserManualProviderId =
        new("d0000000-0000-4000-8000-000000000001");

    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/review")
                       .WithTags("Review Queue");

        // ── GET /review/pending ──────────────────────────────────────────────
        group.MapGet("/pending", async (
            int? limit,
            IReviewQueueRepository reviewRepo,
            CancellationToken ct) =>
        {
            var items = await reviewRepo.GetPendingAsync(limit ?? 50, ct);
            var dtos = items.Select(e => ReviewItemDto.FromDomain(e)).ToList();
            return Results.Ok(dtos);
        })
        .WithName("GetPendingReviews")
        .WithSummary("List pending review queue items.")
        .Produces<List<ReviewItemDto>>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /review/count ────────────────────────────────────────────────
        group.MapGet("/count", async (
            IReviewQueueRepository reviewRepo,
            CancellationToken ct) =>
        {
            var count = await reviewRepo.GetPendingCountAsync(ct);
            return Results.Ok(new ReviewCountResponse { PendingCount = count });
        })
        .WithName("GetReviewCount")
        .WithSummary("Get the number of pending review queue items (for sidebar badge).")
        .Produces<ReviewCountResponse>(StatusCodes.Status200OK)
        .RequireAdminOrCurator();

        // ── GET /review/{id} ─────────────────────────────────────────────────
        group.MapGet("/{id:guid}", async (
            Guid id,
            IReviewQueueRepository reviewRepo,
            CancellationToken ct) =>
        {
            var item = await reviewRepo.GetByIdAsync(id, ct);
            if (item is null)
                return Results.NotFound();

            return Results.Ok(ReviewItemDto.FromDomain(item));
        })
        .WithName("GetReviewItem")
        .WithSummary("Get a single review queue item with full details.")
        .Produces<ReviewItemDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAdminOrCurator();

        // ── POST /review/{id}/resolve ────────────────────────────────────────
        group.MapPost("/{id:guid}/resolve", async (
            Guid id,
            ReviewResolveRequest request,
            IReviewQueueRepository reviewRepo,
            IMetadataClaimRepository claimRepo,
            IHydrationPipelineService pipeline,
            ICanonicalValueRepository canonicalRepo,
            IEventPublisher publisher,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            var item = await reviewRepo.GetByIdAsync(id, ct);
            if (item is null)
                return Results.NotFound();

            if (item.Status != ReviewStatus.Pending)
                return Results.BadRequest("Review item is not pending.");

            // 1. Apply user field overrides (creates user-locked claims).
            if (request.FieldOverrides is { Count: > 0 })
            {
                foreach (var ov in request.FieldOverrides)
                {
                    var claim = new MetadataClaim
                    {
                        Id           = Guid.NewGuid(),
                        EntityId     = item.EntityId,
                        ProviderId   = UserManualProviderId,
                        ClaimKey     = ov.Key,
                        ClaimValue   = ov.Value,
                        Confidence   = 1.0,
                        ClaimedAt    = DateTimeOffset.UtcNow,
                        IsUserLocked = true,
                    };
                    await claimRepo.InsertBatchAsync([claim], ct);
                }
            }

            // 2. If a QID was selected, re-run pipeline with PreResolvedQid.
            if (!string.IsNullOrWhiteSpace(request.SelectedQid))
            {
                var canonicals = await canonicalRepo.GetByEntityAsync(item.EntityId, ct);
                var hints = canonicals.ToDictionary(
                    c => c.Key, c => c.Value, StringComparer.OrdinalIgnoreCase);

                await pipeline.RunSynchronousAsync(new HarvestRequest
                {
                    EntityId       = item.EntityId,
                    EntityType     = EntityType.MediaAsset,
                    MediaType      = Domain.Enums.MediaType.Unknown,
                    Hints          = hints,
                    PreResolvedQid = request.SelectedQid,
                }, ct);
            }

            // 3. Update review queue status.
            await reviewRepo.UpdateStatusAsync(id, ReviewStatus.Resolved, "user", ct);

            // 4. Log activity.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReviewItemResolved,
                EntityId   = item.EntityId,
                Detail     = $"Review item resolved — QID: {request.SelectedQid ?? "none"}, "
                           + $"{request.FieldOverrides?.Count ?? 0} field overrides.",
            }, ct);

            // 5. Broadcast event.
            await publisher.PublishAsync("ReviewItemResolved", new
            {
                review_item_id = id,
                entity_id      = item.EntityId,
                status         = "Resolved",
            }, ct);

            return Results.Ok(new { resolved = true, review_item_id = id });
        })
        .WithName("ResolveReviewItem")
        .WithSummary("Resolve a review queue item: select a QID and/or override fields.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /review/{id}/dismiss ────────────────────────────────────────
        group.MapPost("/{id:guid}/dismiss", async (
            Guid id,
            IReviewQueueRepository reviewRepo,
            IEventPublisher publisher,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            var item = await reviewRepo.GetByIdAsync(id, ct);
            if (item is null)
                return Results.NotFound();

            if (item.Status != ReviewStatus.Pending)
                return Results.BadRequest("Review item is not pending.");

            await reviewRepo.UpdateStatusAsync(id, ReviewStatus.Dismissed, "user", ct);

            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReviewItemResolved,
                EntityId   = item.EntityId,
                Detail     = "Review item dismissed by user.",
            }, ct);

            await publisher.PublishAsync("ReviewItemResolved", new
            {
                review_item_id = id,
                entity_id      = item.EntityId,
                status         = "Dismissed",
            }, ct);

            return Results.Ok(new { dismissed = true, review_item_id = id });
        })
        .WithName("DismissReviewItem")
        .WithSummary("Dismiss a review queue item as irrelevant.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        // ── POST /review/{id}/skip-universe ────────────────────────────────
        group.MapPost("/{id:guid}/skip-universe", async (
            Guid id,
            IReviewQueueRepository reviewRepo,
            IHubRepository hubRepo,
            IEventPublisher publisher,
            ISystemActivityRepository activityRepo,
            CancellationToken ct) =>
        {
            var item = await reviewRepo.GetByIdAsync(id, ct);
            if (item is null)
                return Results.NotFound();

            if (item.Status != ReviewStatus.Pending)
                return Results.BadRequest("Review item is not pending.");

            // 1. Set universe_mismatch flag on the Work.
            await hubRepo.SetUniverseMismatchAsync(item.EntityId, ct);

            // 2. Dismiss the review item.
            await reviewRepo.UpdateStatusAsync(id, ReviewStatus.Dismissed, "user", ct);

            // 3. Log activity.
            await activityRepo.LogAsync(new SystemActivityEntry
            {
                ActionType = SystemActionType.ReviewItemResolved,
                EntityId   = item.EntityId,
                Detail     = "Universe matching skipped by user — Work marked as content-matched only.",
            }, ct);

            // 4. Broadcast event.
            await publisher.PublishAsync("ReviewItemResolved", new
            {
                review_item_id = id,
                entity_id      = item.EntityId,
                status         = "Dismissed",
            }, ct);

            return Results.Ok(new { skipped = true, review_item_id = id });
        })
        .WithName("SkipUniverseMatch")
        .WithSummary("Skip Universe (Wikidata) matching for this item. Sets universe_mismatch on the Work and dismisses the review item.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest)
        .RequireAdminOrCurator();

        return app;
    }
}
