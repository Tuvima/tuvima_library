using MediaEngine.Api.Security;
using MediaEngine.Contracts.Metadata;
using MediaEngine.Domain.Contracts;

namespace MediaEngine.Api.Endpoints;

/// <summary>
/// Endpoints for managing and monitoring the deferred Pass 2 (Universe Lookup)
/// enrichment queue.
///
/// Spec: §3.24 — Two-Pass Enrichment Architecture.
/// </summary>
public static class DeferredEnrichmentEndpoints
{
    public static IEndpointRouteBuilder MapDeferredEnrichmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/metadata/pass2")
                       .WithTags("Deferred Enrichment");

        // ── POST /metadata/pass2/trigger ─────────────────────────────────
        group.MapPost("/trigger", async (
            IDeferredEnrichmentService deferredService,
            CancellationToken ct) =>
        {
            var count = await deferredService.TriggerImmediateProcessingAsync(ct);
            return Results.Ok(new DeferredEnrichmentTriggerResponse(
                count,
                $"Pass 2 triggered — {count} items queued for processing."));
        })
        .WithName("TriggerPass2")
        .WithSummary("Trigger immediate processing of all pending Pass 2 (Universe Lookup) items.")
        .Produces<DeferredEnrichmentTriggerResponse>(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /metadata/pass2/status ───────────────────────────────────
        group.MapGet("/status", async (
            IDeferredEnrichmentService deferredService,
            CancellationToken ct) =>
        {
            var pendingCount = await deferredService.GetPendingCountAsync(ct);
            return Results.Ok(new DeferredEnrichmentStatusResponse(pendingCount, true));
        })
        .WithName("GetPass2Status")
        .WithSummary("Returns the current Pass 2 queue status.")
        .Produces<DeferredEnrichmentStatusResponse>(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }
}
