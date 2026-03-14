using MediaEngine.Api.Security;
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

        // ── POST /metadata/pass2/trigger ────────────────────────────────────
        group.MapPost("/trigger", async (
            IDeferredEnrichmentService deferredService,
            CancellationToken ct) =>
        {
            var count = await deferredService.TriggerImmediateProcessingAsync(ct);
            return Results.Ok(new
            {
                pending_count = count,
                message = $"Pass 2 triggered — {count} items queued for processing.",
            });
        })
        .WithName("TriggerPass2")
        .WithSummary("Trigger immediate processing of all pending Pass 2 (Universe Lookup) items.")
        .Produces(StatusCodes.Status200OK)
        .RequireAdmin();

        // ── GET /metadata/pass2/status ───────────────────────────────────────
        group.MapGet("/status", (
            IDeferredEnrichmentService deferredService) =>
        {
            return Results.Ok(new
            {
                pending_count    = deferredService.PendingCount,
                two_pass_enabled = true,
            });
        })
        .WithName("GetPass2Status")
        .WithSummary("Returns the current Pass 2 queue status.")
        .Produces(StatusCodes.Status200OK)
        .RequireAnyRole();

        return app;
    }
}
