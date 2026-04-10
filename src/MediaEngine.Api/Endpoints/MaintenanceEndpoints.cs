using MediaEngine.Api.Security;
using MediaEngine.Domain.Contracts;
using MediaEngine.Domain.Enums;
using MediaEngine.Ingestion.Services;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Engine-side routes for the auto re-tag sweep:
/// state (pending diff + current hashes), Apply, manual Run Now, pause/resume,
/// and single-asset retry. Used by the Dashboard's Maintenance tab.
/// </summary>
public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        // ── GET /maintenance/retag-sweep/state ────────────────────────────
        // Returns whether a pending diff is staged, and the diff itself so
        // the Dashboard can render the confirmation card.
        app.MapGet("/maintenance/retag-sweep/state", (WritebackConfigState hashState) =>
        {
            return Results.Ok(new
            {
                has_pending_diff = hashState.HasPendingDiff,
                pending_diff     = hashState.PendingDiff.Select(d => new
                {
                    media_type     = d.MediaType,
                    added_fields   = d.AddedFields,
                    removed_fields = d.RemovedFields,
                }).ToArray(),
                current_hashes = hashState.CurrentHashes,
            });
        })
        .WithTags("Maintenance")
        .WithName("GetRetagSweepState")
        .WithSummary("Returns the pending writeback-fields.json diff and current per-media-type hashes.")
        .RequireAdminOrCurator();

        // ── POST /maintenance/retag-sweep/apply ───────────────────────────
        // Commits the staged diff so the worker starts re-tagging. Idempotent.
        app.MapPost("/maintenance/retag-sweep/apply", (WritebackConfigState hashState) =>
        {
            hashState.ApplyPending();
            return Results.Ok(new { applied = true });
        })
        .WithTags("Maintenance")
        .WithName("ApplyRetagSweepPending")
        .WithSummary("Commits the staged writeback field diff so the sweep becomes eligible.")
        .RequireAdmin();

        // ── POST /maintenance/retag-sweep/run-now ─────────────────────────
        // Raises PendingApplied (without applying anything) to wake the
        // worker immediately. Useful when the user wants to retry the
        // existing hash state ahead of the cron schedule.
        app.MapPost("/maintenance/retag-sweep/run-now", (WritebackConfigState hashState) =>
        {
            hashState.SignalRunNow();
            return Results.Ok(new { triggered = true });
        })
        .WithTags("Maintenance")
        .WithName("RunRetagSweepNow")
        .WithSummary("Wakes the retag sweep worker immediately for an out-of-band pass.")
        .RequireAdmin();

        // ── POST /maintenance/retag-sweep/retry/{assetId} ─────────────────
        // Clears the terminal failure flag on a single asset so the next
        // sweep pass picks it up. Also resolves any WritebackFailed review
        // item pointing at that asset.
        app.MapPost("/maintenance/retag-sweep/retry/{assetId:guid}", async (
            Guid assetId,
            IMediaAssetRepository assetRepo,
            IReviewQueueRepository reviewRepo,
            CancellationToken ct) =>
        {
            // Setting the next-retry timestamp to "now" and clearing the
            // failure status unblocks the next GetStaleForRetagAsync sweep.
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await assetRepo.ScheduleRetagRetryAsync(assetId, now, "manual retry", ct);

            // Best-effort resolution of any pending WritebackFailed review items for this asset.
            var pending = await reviewRepo.GetPendingByEntityAsync(assetId, ct);
            foreach (var entry in pending.Where(e => e.Trigger == ReviewTrigger.WritebackFailed))
            {
                await reviewRepo.UpdateStatusAsync(entry.Id, ReviewStatus.Resolved, "manual retry", ct);
            }

            return Results.Ok(new { requeued = true });
        })
        .WithTags("Maintenance")
        .WithName("RetryRetagForAsset")
        .WithSummary("Clears the terminal re-tag failure flag on a single asset so the worker retries it.")
        .RequireAdmin();

        // ── POST /maintenance/initial-sweep/run ───────────────────────────
        // Runs the hash-everything-up-front sweep across every configured
        // library source path. Fire-and-forget — progress is reported over
        // SignalR (InitialSweep{Started,Progress,Completed}). The POST returns
        // as soon as the background task is kicked off.
        // Spec: side-by-side-with-Plex plan §M.
        app.MapPost("/maintenance/initial-sweep/run", (
            IInitialSweepService sweep,
            ILogger<Program> logger) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await sweep.RunAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Initial sweep failed");
                }
            });

            return Results.Accepted(value: new { started = true });
        })
        .WithTags("Maintenance")
        .WithName("RunInitialSweep")
        .WithSummary("Runs the SHA-256 initial sweep across every configured library source path.")
        .RequireAdmin();

        return app;
    }
}
