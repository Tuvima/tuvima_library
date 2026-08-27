using MediaEngine.Api.Http;
using MediaEngine.Api.Security;
using MediaEngine.Api.Services;
using MediaEngine.Contracts.Operations;
using MediaEngine.Contracts.Paging;

namespace MediaEngine.Api.Endpoints;

public static class EnrichmentRefreshEndpoints
{
    public static IEndpointRouteBuilder MapEnrichmentRefreshEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ingestion/refresh-schedule")
            .WithTags("Ingestion")
            .RequireAnyRole();

        group.MapGet("/", async (
            string? entityType,
            string? status,
            int? limit,
            EnrichmentRefreshScheduleService schedule,
            CancellationToken ct) => Results.Ok(await schedule.GetAsync(
                entityType,
                status,
                PagedRequest.From(null, limit, defaultLimit: 250, maxLimit: 1000).Limit,
                ct)))
            .WithName("GetEnrichmentRefreshSchedule")
            .WithSummary("Upcoming and active recurring enrichment refreshes.")
            .Produces<EnrichmentRefreshScheduleResponse>();

        group.MapPost("/{entityType}/{entityId:guid}/run-now", async (
            string entityType,
            Guid entityId,
            EnrichmentRefreshScheduleService schedule,
            CancellationToken ct) =>
        {
            var queued = await schedule.QueueNowAsync(entityType, entityId, "Manual", ct);
            return queued is null
                ? ApiErrors.NotFound($"Refresh target '{entityType}/{entityId}' was not found.")
                : Results.Accepted(value: queued);
        })
        .WithName("RunEnrichmentRefreshNow")
        .WithSummary("Queue an entity for the full enrichment cycle now.")
        .Produces<EnrichmentRefreshQueuedResponse>(StatusCodes.Status202Accepted)
        .RequireAdminOrStandardUser();

        return app;
    }
}
