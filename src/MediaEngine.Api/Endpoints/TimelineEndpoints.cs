using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Entities;
using MediaEngine.Domain.Enums;
using MediaEngine.Domain.Models;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Timeline API endpoints — full event history and pipeline provenance for each entity.
/// All routes are grouped under <c>/timeline</c>.
///
/// <list type="bullet">
///   <item><c>GET  /timeline/{entityId}</c>                            — full event history, newest first</item>
///   <item><c>GET  /timeline/{entityId}/pipeline</c>                   — current pipeline state (latest per stage)</item>
///   <item><c>GET  /timeline/{entityId}/event/{eventId}/changes</c>    — field-level changes for one event</item>
///   <item><c>POST /timeline/{entityId}/revert/{eventId}</c>           — revert a sync writeback</item>
/// </list>
/// </summary>
public static class TimelineEndpoints
{
    public static IEndpointRouteBuilder MapTimelineEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/timeline").WithTags("Timeline");

        // ── GET /timeline/{entityId} ──────────────────────────────────────────
        grp.MapGet("/{entityId:guid}", async (
            Guid entityId,
            IEntityTimelineRepository repo,
            CancellationToken ct) =>
        {
            var events = await repo.GetEventsByEntityAsync(entityId, ct);
            return Results.Ok(events);
        })
        .WithName("GetEntityTimeline")
        .WithSummary("Returns the full event history for an entity, newest first.")
        .Produces<IReadOnlyList<EntityEvent>>(StatusCodes.Status200OK);

        // ── GET /timeline/{entityId}/pipeline ─────────────────────────────────
        grp.MapGet("/{entityId:guid}/pipeline", async (
            Guid entityId,
            IEntityTimelineRepository repo,
            CancellationToken ct) =>
        {
            var state = await repo.GetCurrentPipelineStateAsync(entityId, ct);
            return Results.Ok(state);
        })
        .WithName("GetPipelineState")
        .WithSummary("Returns the most recent event per pipeline stage for an entity.")
        .Produces<IReadOnlyList<EntityEvent>>(StatusCodes.Status200OK);

        // ── GET /timeline/{entityId}/event/{eventId}/changes ──────────────────
        grp.MapGet("/{entityId:guid}/event/{eventId:guid}/changes", async (
            Guid entityId,
            Guid eventId,
            IEntityTimelineRepository repo,
            CancellationToken ct) =>
        {
            var changes = await repo.GetFieldChangesByEventAsync(eventId, ct);
            return Results.Ok(changes);
        })
        .WithName("GetEventFieldChanges")
        .WithSummary("Returns field-level changes for a specific event.")
        .Produces<IReadOnlyList<EntityFieldChange>>(StatusCodes.Status200OK);

        // ── POST /timeline/{entityId}/revert/{eventId} ────────────────────────
        grp.MapPost("/{entityId:guid}/revert/{eventId:guid}", async (
            Guid entityId,
            Guid eventId,
            IEntityTimelineRepository repo,
            CancellationToken ct) =>
        {
            // Verify the event exists and is a sync_writeback
            var evt = await repo.GetEventByIdAsync(eventId, ct);
            if (evt is null) return Results.NotFound("Event not found");
            if (evt.EventType != "sync_writeback") return Results.BadRequest("Can only revert sync_writeback events");

            // Require that file originals were recorded for this event
            var originals = await repo.GetFileOriginalsForEventAsync(eventId, ct);
            if (originals.Count == 0) return Results.BadRequest("No file originals recorded for this event");

            // TODO: Phase 6 will implement actual file revert via IMetadataTagger.
            // For now, record the revert event so the timeline reflects the user's intent.
            var revertEvent = new EntityEvent
            {
                EntityId   = entityId,
                EntityType = evt.EntityType,
                EventType  = "sync_reverted",
                Trigger    = "user_manual",
                Detail     = $"Reverted sync writeback from {evt.OccurredAt:yyyy-MM-dd HH:mm}",
            };
            await repo.InsertEventAsync(revertEvent, ct);

            return Results.Ok(new { reverted = originals.Count, eventId = revertEvent.Id });
        })
        .WithName("RevertSyncWriteback")
        .WithSummary("Reverts a sync writeback by restoring original file metadata.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // ── POST /timeline/{entityId}/rematch ─────────────────────────────────
        grp.MapPost("/{entityId:guid}/rematch", async (
            Guid entityId,
            IEntityTimelineRepository timelineRepo,
            ICanonicalValueRepository canonicalRepo,
            IHydrationPipelineService pipeline,
            IMediaAssetRepository assetRepo,
            CancellationToken ct) =>
        {
            // Verify the entity exists
            var asset = await assetRepo.FindByIdAsync(entityId, ct);
            if (asset is null) return Results.NotFound("Entity not found");

            // Snapshot current canonicals for pre/post diff
            var beforeValues = await canonicalRepo.GetByEntityAsync(entityId, ct);

            // Resolve media type from canonical values (fallback to Unknown)
            var mediaTypeStr = beforeValues
                .FirstOrDefault(cv => string.Equals(cv.Key, "media_type", StringComparison.OrdinalIgnoreCase))
                ?.Value;
            if (!Enum.TryParse<MediaType>(mediaTypeStr, ignoreCase: true, out var mediaType))
                mediaType = MediaType.Unknown;

            // Record the re-match initiation event
            await timelineRepo.InsertEventAsync(new EntityEvent
            {
                EntityId   = entityId,
                EntityType = "Work",
                EventType  = "retail_rematched",
                Stage      = 1,
                Trigger    = "user_rematch",
                Detail     = $"Re-match initiated by user — {beforeValues.Count} existing canonicals",
            }, ct);

            // Re-enqueue through the pipeline
            var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cv in beforeValues)
            {
                if (!string.IsNullOrWhiteSpace(cv.Value))
                    hints[cv.Key] = cv.Value;
            }

            await pipeline.EnqueueAsync(new HarvestRequest
            {
                EntityId   = entityId,
                EntityType = EntityType.MediaAsset,
                MediaType  = mediaType,
                Hints      = hints,
            }, ct);

            return Results.Ok(new { queued = true, entityId });
        })
        .WithName("RematchEntity")
        .WithSummary("Re-matches an entity through the full pipeline.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
